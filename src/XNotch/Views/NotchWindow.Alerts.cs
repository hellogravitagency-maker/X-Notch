using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XNotch.Controls;
using XNotch.Helpers;
using XNotch.Island;
using XNotch.Island.Activities;
using XNotch.Models;
using XNotch.Services;
using Orientation = System.Windows.Controls.Orientation;

namespace XNotch.Views;

/// <summary>Dynamic Island: alert sources, the volume / brightness HUD and the Spotlight morph.</summary>
public partial class NotchWindow
{
    private int _lastBatteryPercent = -1;
    private DeckState _lastDeckState = DeckState.Off;
    private long _hudArmedAt;

    // HUD view parts (updated in place while the HUD stays up)
    private bool _hudIsBrightness;
    private double _hudLevel;
    private bool _hudMuted;
    private TextBlock? _hudIcon;
    private Border? _hudFill;
    private RollingNumberText? _hudNumber;

    /// <summary>Wires existing service events to island alerts / HUD (after services are up).</summary>
    private void WireIslandSources()
    {
        // Ignore the burst of change events services raise while starting up.
        _hudArmedAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3 / 2;

        // Game / fullscreen mode: only low battery alerts, held ones expire after 10 s
        _fullscreenService.FullscreenChanged += fs => Dispatcher.BeginInvoke(() => _island?.SetGameMode(fs));
        _island?.SetGameMode(_fullscreenService.IsFullscreen);

        // Low battery at 20 % and 10 % while discharging (threshold on the existing reading)
        _batteryService.PropertyChanged += OnBatteryChanged;

        // Default audio output changed
        _audioCaptureService.DefaultRenderDeviceChanged += name => Dispatcher.BeginInvoke(() =>
        {
            if (IsDynamicTop && !string.IsNullOrWhiteSpace(name))
                PostIslandAlert(new IslandAlert("audio:" + name, Loc.T("Audio"), "→ " + name, ""));
        });

        // Phone Deck: pairing already raises a notification (→ alert); the server going off is new
        if (_services.Deck is { } deck)
        {
            _lastDeckState = deck.State;
            deck.StateChanged += () => Dispatcher.BeginInvoke(() =>
            {
                var s = deck.State;
                if (IsDynamicTop && s != _lastDeckState)
                {
                    if (s == DeckState.On) PostIslandAlert(new IslandAlert("deck:on", Loc.T("Phone Deck"), Loc.T("Connected"), "", AccentHex: IslandColors.Success));
                    else if (_lastDeckState == DeckState.On) PostIslandAlert(new IslandAlert("deck:off", Loc.T("Phone Deck"), Loc.T("Disconnected"), ""));
                }
                _lastDeckState = s;
            });
        }

        // Daily briefing ready (smart reminders already arrive as notifications → alerts)
        _services.Briefing.BriefingReady += report => Dispatcher.BeginInvoke(() =>
        {
            if (IsDynamicTop) PostIslandAlert(new IslandAlert("briefing", Loc.T("Daily briefing ready"), report.Headline, "", AccentHex: IslandColors.Info));
        });

        // HUD: same Windows hooks the classic HUD uses
        _volumeService.VolumeChanged += (v, muted) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsDynamicTop || !_settings.ShowVolumeHud) return;
            ShowIslandHud(brightness: false, v, muted);
        });
        if (_brightnessService.IsSupported)
        {
            _brightnessService.BrightnessChanged += b => Dispatcher.BeginInvoke(() =>
            {
                if (!IsDynamicTop || !_settings.ShowBrightnessHud) return;
                ShowIslandHud(brightness: true, b / 100.0, false);
            });
        }

        // Spotlight: the island grows when results appear and morphs back on dismiss
        if (_spotlightLauncher != null)
        {
            _spotlightLauncher.IslandResultsChanged += show =>
            {
                _spotlightHasResults = show;
                RetargetIsland(force: true);
            };
            _spotlightLauncher.Dismissed += () =>
            {
                _spotlightHasResults = false;
                if (_island?.SpotlightOpen == true) _island.CloseSpotlight();
            };
        }
    }

    private void OnBatteryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BatteryService.ChargePercent) or nameof(BatteryService.IsCharging))) return;
        Dispatcher.BeginInvoke(() =>
        {
            int now = _batteryService.ChargePercent;
            int before = _lastBatteryPercent;
            _lastBatteryPercent = now;
            if (!IsDynamicTop || !_batteryService.HasBattery || _batteryService.IsCharging || before < 0) return;
            foreach (int threshold in new[] { 20, 10 })
            {
                if (before > threshold && now <= threshold)
                {
                    PostIslandAlert(new IslandAlert("battery:" + threshold, Loc.T("Low Battery"),
                        Loc.T("{0}% remaining", now), "", IsLowBattery: true, Value: now, AccentHex: IslandColors.LowBattery));
                    break;
                }
            }
        });
    }

    private void ShowIslandHud(bool brightness, double level, bool muted)
    {
        if (_island == null || Stopwatch.GetTimestamp() < _hudArmedAt) return;
        _hudIsBrightness = brightness;
        _hudLevel = Math.Clamp(level, 0, 1);
        _hudMuted = muted;
        UpdateHudView();
        _island.PingHud(); // (re)starts the 1.5 s window
        _islandTimer?.Start();
    }

    /// <summary>Icon + 4 px slider + rolling percentage, in the alert geometry.</summary>
    private FrameworkElement BuildHudView()
    {
        _hudIcon = IslandVisuals.Glyph("", 16); // icon font; UpdateHudView picks the level glyph
        var iconHost = new Border
        {
            CornerRadius = new CornerRadius(IslandMetrics.AlertIcon / 2),
            Background = IslandVisuals.Brush("#26FFFFFF"),
            Child = _hudIcon,
        };

        _hudFill = new Border
        {
            Height = IslandMetrics.HudSliderHeight,
            CornerRadius = new CornerRadius(IslandMetrics.HudSliderHeight / 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = new ScaleTransform(0, 1),
        };
        var track = new Border
        {
            Height = IslandMetrics.HudSliderHeight,
            CornerRadius = new CornerRadius(IslandMetrics.HudSliderHeight / 2),
            Background = IslandVisuals.Brush(IslandColors.ScrubberTrack),
            Child = _hudFill,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _hudNumber = new RollingNumberText(IslandMetrics.NumberFontSize, FontWeights.SemiBold);
        var number = new StackPanel { Orientation = Orientation.Horizontal, MinWidth = 48, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        number.Children.Add(_hudNumber);
        number.Children.Add(IslandVisuals.Text("%", IslandMetrics.NumberFontSize, bold: true, secondary: true));

        var g = new Grid { Margin = new Thickness(IslandMetrics.AlertPadding, 0, IslandMetrics.AlertPadding, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandMetrics.AlertIcon) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        iconHost.Height = IslandMetrics.AlertIcon;
        g.Children.Add(iconHost);
        track.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(track, 1); g.Children.Add(track);
        Grid.SetColumn(number, 2); g.Children.Add(number);

        UpdateHudView();
        return g;
    }

    private void UpdateHudView()
    {
        if (_hudIcon == null || _hudFill == null || _hudNumber == null) return;
        bool silent = !_hudIsBrightness && (_hudMuted || _hudLevel < 0.01);
        string accent = _hudIsBrightness ? "#FFD60A" : silent ? IslandColors.Destructive : IslandColors.TextPrimary;
        _hudIcon.Text = _hudIsBrightness ? "" : silent ? "" : _hudLevel < 0.33 ? "" : _hudLevel < 0.66 ? "" : "";
        _hudIcon.Foreground = IslandVisuals.Brush(accent);
        _hudFill.Background = IslandVisuals.Brush(accent);
        ((ScaleTransform)_hudFill.RenderTransform).ScaleX = silent ? 0 : _hudLevel;
        _hudNumber.Text = ((int)Math.Round((silent ? 0 : _hudLevel) * 100)).ToString();
    }

    /// <summary>Ctrl+Space in Dynamic mode: the island morphs into the search field.</summary>
    private bool TryOpenSpotlightInIsland()
    {
        if (!IsDynamicTop || _island == null || _spotlightLauncher == null) return false;
        _island.OpenSpotlight();

        // Let the shape lead, then lay the field over it (its black is the island's own fill).
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IslandMetrics.ContentEnterDelayMs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_island?.SpotlightOpen != true) return;
            double windowW = ActualWidth > 0 ? ActualWidth : Width;
            double x = (windowW - IslandMetrics.SpotlightWidth) / 2;
            double y = IsFloating ? IslandMetrics.FloatingTopOffset + 2 : 0;
            var topLeft = NotchCanvas.PointToScreen(new Point(x, y));
            var bottomRight = NotchCanvas.PointToScreen(new Point(x + IslandMetrics.SpotlightWidth, y + IslandMetrics.SpotlightResultsHeight));
            _spotlightLauncher.OpenInIsland((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
                (int)Math.Round(bottomRight.X - topLeft.X), (int)Math.Round(bottomRight.Y - topLeft.Y));
        };
        t.Start();
        return true;
    }
}
