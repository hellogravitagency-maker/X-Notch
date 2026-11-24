using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using XNotch.Controls;
using XNotch.Helpers;
using XNotch.Island;
using XNotch.Island.Activities;

using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using FlowDirection = System.Windows.FlowDirection;
using ActivitySource = XNotch.Island.ActivitySource;

namespace XNotch.Views;

/// <summary>Dynamic Island activities: compact / minimal / alert views, goo circle, assistant glow, pages.</summary>
public partial class NotchWindow
{
    private readonly List<ILiveActivity> _liveActivities = [];
    private TimerActivity? _timerActivity;
    private bool _activitiesBuilt;
    private bool _spotlightHasResults;

    // Minimal: content of the detached circle, and the merge → swap → split click
    private Grid? _minimalCircleHost;
    private string? _minimalCircleKey;
    private bool _swapPending;

    // Assistant glow (agent running)
    private AssistantGlow? _assistantGlow;
    private Path? _assistantRing, _assistantHalo;
    private readonly long _glowOrigin = Stopwatch.GetTimestamp();

    // Expanded pages owned by activities (timer, plugins)
    private readonly Dictionary<string, FrameworkElement> _activityPages = [];

    private void EnsureActivities()
    {
        if (_activitiesBuilt) return;
        _activitiesBuilt = true;
        _timerActivity = new TimerActivity(_services.Timer);
        AddLiveActivity(new MusicActivity(_mediaService, _audioCaptureService, () => _settings.ShowMusicControls));
        AddLiveActivity(_timerActivity);
        AddLiveActivity(new StopwatchActivity(StopwatchWidget));
        AddLiveActivity(new AgentActivity(_services.Agents));
        AddLiveActivity(new ShelfActivity(_shelfService));
    }

    /// <summary>Registers a live activity (built-in or plugin).</summary>
    internal void AddLiveActivity(ILiveActivity activity)
    {
        if (_liveActivities.Any(a => a.Id == activity.Id)) return;
        _liveActivities.Add(activity);
        if (_subscribedActivities.Add(activity)) // plugins may hand back the same instance after a re-inject
            activity.Changed += () => Dispatcher.BeginInvoke(RefreshIslandActivities);
    }

    internal void RemoveLiveActivity(string id)
    {
        _liveActivities.RemoveAll(a => a.Id == id);
        RefreshIslandActivities();
    }

    private readonly HashSet<string> _pluginActivityIds = [];
    private readonly HashSet<ILiveActivity> _subscribedActivities = [];

    /// <summary>(Re)loads activities from enabled ILiveActivityPlugins.</summary>
    private void InjectPluginActivities()
    {
        if (_pluginManager == null) return;
        foreach (var id in _pluginActivityIds) _liveActivities.RemoveAll(a => a.Id == id);
        _pluginActivityIds.Clear();
        foreach (var plugin in _pluginManager.GetLiveActivityPlugins())
        {
            try
            {
                foreach (var activity in plugin.GetLiveActivities())
                {
                    if (_liveActivities.Any(a => a.Id == activity.Id)) continue;
                    AddLiveActivity(activity);
                    _pluginActivityIds.Add(activity.Id);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Live activities of plugin {plugin.Name} failed: {ex.Message}");
            }
        }
        RefreshIslandActivities();
    }

    /// <summary>IIslandAlerts for plugins: island alert in Dynamic mode, notification otherwise.</summary>
    private sealed class IslandAlertsForPlugins(NotchWindow window) : Plugins.IIslandAlerts
    {
        public void Post(string title, string? subtitle = null, string? icon = null, string? key = null) =>
            window.Dispatcher.BeginInvoke(() =>
            {
                if (window.IsDynamicTop) window.PostIslandAlert(new IslandAlert("plugin:" + (key ?? title), title, subtitle, icon));
                else window._services.Notifications.AddNotification("X-Notch", title, subtitle ?? "");
            });
    }

    private ILiveActivity? FindActivity(string? id) => id == null ? null : _liveActivities.FirstOrDefault(a => a.Id == id);

    /// <summary>Feeds the active activities to the presentation machine.</summary>
    private void RefreshIslandActivities()
    {
        if (_island == null) return;
        EnsureActivities();
        var entries = new List<ActivityEntry>();
        foreach (var a in _liveActivities)
        {
            bool active;
            try { active = a.IsActive; } catch { active = false; } // a misbehaving plugin can't break the island
            if (active)
                entries.Add(new ActivityEntry(a.Id, a.Priority, a.StartedAt,
                    a is LiveActivityBase ? ActivitySource.BuiltIn : ActivitySource.Plugin));
        }
        _island.SetActivities(entries);
    }

    /// <summary>Builds the view for a presentation (shown in the island content layer).</summary>
    private FrameworkElement? BuildIslandContent(IslandPresentation p)
    {
        try
        {
            switch (p)
            {
                case IslandPresentation.Compact:
                case IslandPresentation.Minimal:
                    var a = FindActivity(_island?.Primary?.Id);
                    return a == null ? null : IslandVisuals.CompactRow(a.CreateCompactLeading(), a.CreateCompactTrailing());
                case IslandPresentation.Alert:
                    return _island?.Alerts.Current is { } alert ? BuildAlertView(alert) : null;
                case IslandPresentation.Hud:
                    return BuildHudView();
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Island view failed: {ex.Message}");
            return null;
        }
    }

    private static FrameworkElement BuildAlertView(IslandAlert alert)
    {
        string accent = alert.AccentHex ?? (alert.IsLowBattery ? IslandColors.LowBattery : IslandColors.TextPrimary);
        var icon = new Border
        {
            CornerRadius = new CornerRadius(IslandMetrics.AlertIcon / 2),
            Background = IslandVisuals.Brush("#26FFFFFF"),
            Child = IslandVisuals.Glyph(alert.Icon ?? "", 16, accent),
        };
        FrameworkElement? trailing = null;
        if (alert.Value is int v)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new RollingNumberText(IslandMetrics.NumberFontSize, FontWeights.SemiBold, IslandVisuals.Brush(accent)) { Text = v.ToString() });
            panel.Children.Add(IslandVisuals.Text("%", IslandMetrics.NumberFontSize, bold: true, secondary: true));
            trailing = panel;
        }
        return IslandVisuals.AlertRow(icon, alert.Title, alert.Subtitle, trailing);
    }

    /// <summary>Per-frame extras: swap choreography and the assistant glow rotation.</summary>
    private bool TickIslandExtras()
    {
        bool moving = false;
        if (_swapPending && _goo < 0.08)
        {
            _swapPending = false;
            _island?.SwapPrimary(); // content swaps while merged, then AimGoo splits again
            RetargetIsland(force: true);
        }

        if (_assistantRing is { Visibility: Visibility.Visible } && _assistantGlow != null
            && !SpringAnimator.Reduced && !_fullscreenService.IsFullscreen && IsVisible)
        {
            _assistantGlow.SetTime((Stopwatch.GetTimestamp() - _glowOrigin) / (double)Stopwatch.Frequency);
            moving = true;
        }
        return moving;
    }

    private void PulseTimerRing() => _timerActivity?.Pulse();

    /// <summary>Positions the detached circle content and the assistant ring for this frame.</summary>
    private void LayoutMinimalCircle(double offsetX, double offsetY, double w, double h)
    {
        if (_island == null) return;

        // Detached circle (Minimal secondary)
        if (_minimalCircleHost == null)
        {
            _minimalCircleHost = new Grid
            {
                Width = IslandMetrics.MinimalCircle,
                Height = IslandMetrics.MinimalCircle,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            _minimalCircleHost.MouseLeftButtonUp += (_, e) =>
            {
                if (_island?.Presentation != IslandPresentation.Minimal || SpringAnimator.Reduced && _island == null) return;
                e.Handled = true;
                if (SpringAnimator.Reduced) { _island.SwapPrimary(); RetargetIsland(force: true); return; }
                _swapPending = true; // merge → swap → split
                Aim(_gooSpring.Use(IslandSprings.GooSplit), 0, _goo);
            };
            NotchCanvas.Children.Add(_minimalCircleHost);
        }

        string? secondary = _island.Presentation == IslandPresentation.Minimal ? _island.Secondary?.Id : null;
        if (secondary != _minimalCircleKey)
        {
            _minimalCircleKey = secondary;
            _minimalCircleHost.Children.Clear();
            if (FindActivity(secondary)?.CreateMinimalView() is { } view)
            {
                view.HorizontalAlignment = HorizontalAlignment.Center;
                view.VerticalAlignment = VerticalAlignment.Center;
                _minimalCircleHost.Children.Add(view);
            }
        }
        var c = MinimalCircleCenter(offsetX, offsetY, w, h);
        Canvas.SetLeft(_minimalCircleHost, c.X - IslandMetrics.MinimalCircle / 2);
        Canvas.SetTop(_minimalCircleHost, c.Y - IslandMetrics.MinimalCircle / 2);
        double shown = Math.Clamp((_goo - 0.6) / 0.4, 0, 1); // content appears once the droplet has detached
        _minimalCircleHost.Opacity = shown;
        _minimalCircleHost.Visibility = _goo > 0.01 && IsFloating ? Visibility.Visible : Visibility.Collapsed;
        _minimalCircleHost.IsHitTestVisible = shown > 0.5;

        LayoutAssistantGlow(offsetX, offsetY);
    }

    private void LayoutAssistantGlow(double offsetX, double offsetY)
    {
        bool agent = _island!.Ordered.Any(e => e.Id == "agent") && _island.Presentation != IslandPresentation.Spotlight;
        if (_assistantRing == null)
        {
            if (!agent) return;
            _assistantGlow = new AssistantGlow();
            _assistantHalo = new Path
            {
                Stroke = _assistantGlow.Brush,
                StrokeThickness = IslandMetrics.AssistantRing * 3,
                IsHitTestVisible = false,
                Effect = new BlurEffect { Radius = IslandMetrics.AssistantBlur, RenderingBias = RenderingBias.Performance },
            };
            _assistantRing = new Path { Stroke = _assistantGlow.Brush, StrokeThickness = IslandMetrics.AssistantRing, IsHitTestVisible = false };
            NotchCanvas.Children.Insert(NotchCanvas.Children.IndexOf(NotchPath), _assistantHalo);
            NotchCanvas.Children.Insert(NotchCanvas.Children.IndexOf(NotchPath) + 1, _assistantRing);
        }

        var vis = agent ? Visibility.Visible : Visibility.Collapsed;
        _assistantRing.Visibility = _assistantHalo!.Visibility = vis;
        if (!agent) return;
        _assistantRing.Data = _assistantHalo.Data = NotchPath.Data;
        Canvas.SetLeft(_assistantRing, offsetX); Canvas.SetTop(_assistantRing, offsetY);
        Canvas.SetLeft(_assistantHalo, offsetX); Canvas.SetTop(_assistantHalo, offsetY);
        HookRendering(); // rotation runs while the ring is up
    }

    /// <summary>
    /// Dynamic mode: activity pages go first in the carousel (primary first). Built-ins
    /// reuse their existing cards; activities with their own expanded view get a page.
    /// </summary>
    private void PutActivityPagesFirst()
    {
        if (_island == null || !IsDynamicTop) return;

        var live = _island.Ordered.Select(e => e.Id).ToList();
        foreach (var id in _activityPages.Keys.Where(k => !live.Contains(k)).ToList())
        {
            CarouselCardHost.Children.Remove(_activityPages[id]);
            _activityPages.Remove(id);
        }

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var (card, title) = CardForActivity(live[i]);
            if (card == null) continue;
            int at = _enabledCards.FindIndex(c => c.Element == card);
            if (at >= 0) { title = _enabledCards[at].Title; _enabledCards.RemoveAt(at); }
            _enabledCards.Insert(0, (card, title));
        }
    }

    private (FrameworkElement? Card, string Title) CardForActivity(string id)
    {
        switch (id)
        {
            case "media": return (MusicPlayer, "🎵 " + Loc.T("Music"));
            case "stopwatch": return (StopwatchWidget, "⏱ " + Loc.T("Stopwatch"));
            case "agent": return (AgentsWidget, "✦ " + Loc.T("Agents"));
            case "shelf": return (null, ""); // the shelf row already shows in the expanded island
        }
        if (!_activityPages.TryGetValue(id, out var page))
        {
            FrameworkElement? view = null;
            try { view = FindActivity(id)?.CreateExpandedView(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
            if (view == null) return (null, "");
            view.Visibility = Visibility.Collapsed;
            CarouselCardHost.Children.Add(view);
            _activityPages[id] = page = view;
        }
        return (page, id == "timer" ? "◔ " + Loc.T("Timer") : id);
    }

}
