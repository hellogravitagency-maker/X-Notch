using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using XNotch.Helpers;

namespace XNotch.Views;

public partial class AgentsWidgetView : UserControl
{
    private AgentHubService? _hub;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public AgentsWidgetView()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => Refresh();
        // ponytail: tick only while the card is on screen; events cover the rest.
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _clock.Start(); } else _clock.Stop(); };
    }

    public void Bind(AgentHubService hub)
    {
        if (_hub != null) _hub.SessionsChanged -= OnChanged;
        _hub = hub;
        _hub.SessionsChanged += OnChanged;
        Refresh();
    }

    /// <summary>Shown in the empty state so the user knows whether hooks are wired.</summary>
    public void SetEnabled(bool enabled)
    {
        EmptyTitle.Text = enabled ? Loc.T("No agents running") : Loc.T("Agents are off");
        EmptyHint.Text = enabled
            ? Loc.T("Start Claude Code or Codex and it shows up here")
            : Loc.T("Turn on Agents in settings to see Claude Code and Codex live");
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ListScroll.ScrollToVerticalOffset(Math.Clamp(ListScroll.VerticalOffset - e.Delta / 2.5, 0, ListScroll.ScrollableHeight));
        e.Handled = true;
    }

    private void Refresh()
    {
        var sessions = _hub?.Sessions ?? [];
        int active = sessions.Count(s => s.State is AgentState.Working or AgentState.Waiting);
        CountText.Text = Loc.T("{0} active", active);
        EmptyState.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ListPanel.Children.Clear();
        foreach (var s in sessions) ListPanel.Children.Add(BuildRow(s));
    }

    public static Color StateColor(AgentState state) => state switch
    {
        AgentState.Working => Color.FromRgb(0x0A, 0x84, 0xFF),
        AgentState.Waiting => Color.FromRgb(0xFF, 0x9F, 0x0A),
        AgentState.Done => Color.FromRgb(0x30, 0xD1, 0x58),
        AgentState.Error => Color.FromRgb(0xFF, 0x45, 0x3A),
        _ => Color.FromRgb(0x8E, 0x8E, 0x93),
    };

    private FrameworkElement BuildRow(AgentSession s)
    {
        var color = StateColor(s.State);

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7, Fill = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        if (s.State == AgentState.Working && SystemParameters.ClientAreaAnimation) // off = reduced motion
        {
            // Working = gentle breathing dot (state indication, not decoration).
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }

        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(s.Name) || s.Kind == AgentKind.Custom ? s.KindLabel : $"{s.KindLabel} · {s.Name}",
            FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };
        var activity = new TextBlock
        {
            Text = s.Activity, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = s.State == AgentState.Waiting ? new SolidColorBrush(color) : (Brush)FindResource("TextTertiaryBrush"),
        };
        var text = new StackPanel { Children = { title, activity } };

        var elapsed = new TextBlock
        {
            Text = s.State is AgentState.Working ? Format(DateTime.Now - s.StartedAt) : StateLabel(s.State),
            FontSize = 10, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            Foreground = (Brush)FindResource("TextMutedBrush"),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(elapsed, 2);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        grid.Children.Add(elapsed);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 4), Background = (Brush)FindResource("SurfaceBrush"),
            Cursor = Cursors.Hand, ToolTip = Loc.T("Show its terminal"),
        };
        row.MouseLeftButtonUp += (_, _) => Helpers.TerminalFocusHelper.FocusAnyTerminal();
        return row;
    }

    private static string StateLabel(AgentState state) => state switch
    {
        AgentState.Waiting => Loc.T("needs you"),
        AgentState.Done => Loc.T("done"),
        AgentState.Error => Loc.T("error"),
        _ => Loc.T("idle"),
    };

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
