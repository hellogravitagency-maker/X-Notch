using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XNotch.Services;
using XNotch.Helpers;

namespace XNotch.Views;

/// <summary>
/// Agent Live Activity on the closed island, following Apple's Live Activity model:
/// compact (leading + trailing) when it is the only activity, minimal (one dot) when
/// music already owns the compact slot. Tap opens the Agents card.
/// </summary>
public partial class NotchWindow
{
    private DispatcherTimer? _agentTick;

    /// <summary>Returns true when the compact agent presentation occupies the centre slot.</summary>
    private bool UpdateAgentActivity(bool musicOwnsCompact)
    {
        var live = _services.Agents.Sessions
            .Where(s => s.State is AgentState.Working or AgentState.Waiting)
            .OrderByDescending(s => s.State == AgentState.Waiting) // the one that needs you wins
            .FirstOrDefault();

        if (live == null)
        {
            Hide(AgentCompact);
            AgentMinimalDot.Visibility = Visibility.Collapsed;
            _agentTick?.Stop();
            return false;
        }

        var brush = new SolidColorBrush(AgentsWidgetView.StateColor(live.State));
        int others = _services.Agents.Sessions.Count(s => s.State is AgentState.Working or AgentState.Waiting) - 1;

        if (musicOwnsCompact)
        {
            Hide(AgentCompact);
            AgentMinimalDot.Fill = brush;
            AgentMinimalDot.ToolTip = $"{live.KindLabel}: {live.Activity}";
            AgentMinimalDot.Visibility = Visibility.Visible;
            Breathe(AgentMinimalDot, live.State == AgentState.Working);
        }
        else
        {
            AgentMinimalDot.Visibility = Visibility.Collapsed;
            AgentCompactDot.Fill = brush;
            AgentCompactName.Text = others > 0 ? $"{live.KindLabel} +{others}" : live.KindLabel;
            AgentCompactTrailing.Foreground = brush;
            AgentCompactTrailing.Text = live.State == AgentState.Waiting
                ? Loc.T("needs you")
                : FormatElapsed(DateTime.Now - live.StartedAt);
            AgentCompact.ToolTip = live.Activity;
            Breathe(AgentCompactDot, live.State == AgentState.Working);
            ShowWithEntrance(AgentCompact);
        }

        // Elapsed time only ticks while something is working; nothing runs when idle.
        _agentTick ??= CreateAgentTick();
        if (live.State == AgentState.Working) _agentTick.Start(); else _agentTick.Stop();

        return !musicOwnsCompact;
    }

    private DispatcherTimer CreateAgentTick()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) => UpdateClosedContentVisibility();
        return t;
    }

    private void OnAgentCompactClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        TransitionToOpen();
        NavigateToCard(AgentsWidget);
    }

    // Entrance: scale 0.96 + fade, 180 ms strong ease-out. Never from scale(0).
    private static void ShowWithEntrance(FrameworkElement el)
    {
        if (el.Visibility == Visibility.Visible) return;
        el.Visibility = Visibility.Visible;
        if (!SystemParameters.ClientAreaAnimation) return;

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(180);
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        if (el.RenderTransform is ScaleTransform st)
        {
            var s = new DoubleAnimation(0.96, 1, dur) { EasingFunction = ease };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, s);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, s);
        }
    }

    private static void Hide(FrameworkElement el)
    {
        el.BeginAnimation(OpacityProperty, null);
        el.Visibility = Visibility.Collapsed;
    }

    private static void Breathe(UIElement dot, bool on)
    {
        if (!on || !SystemParameters.ClientAreaAnimation) { dot.BeginAnimation(OpacityProperty, null); dot.Opacity = 1; return; }
        if (dot.HasAnimatedProperties) return; // already breathing; don't restart mid-cycle
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private static string FormatElapsed(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
