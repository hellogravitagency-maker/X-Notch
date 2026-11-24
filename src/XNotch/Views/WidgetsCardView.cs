using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XNotch.Models;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using XNotch.Helpers;

namespace XNotch.Views;

/// <summary>
/// Glanceable tiles (weather, air, currency, ping, screen time, countdown, world clock,
/// focus timer, pet). One big value + one small label each, readable at a glance.
/// </summary>
public sealed class WidgetsCardView : UserControl
{
    private readonly UniformGrid _grid = new() { Rows = 2, Columns = 5, Margin = new Thickness(4, 0, 4, 0) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private WidgetsService? _widgets;
    private CountdownTimerService? _timer;
    private AppSettings _settings = new();
    private readonly TranslateTransform _petBob = new();

    public WidgetsCardView()
    {
        Height = 128;
        Background = Brushes.Transparent;
        Content = _grid;
        _tick.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Refresh(); _tick.Start(); _ = _widgets?.RefreshAsync(); }
            else _tick.Stop();
        };
    }

    public void Bind(WidgetsService widgets, CountdownTimerService timer, AppSettings settings)
    {
        _widgets = widgets;
        _timer = timer;
        _settings = settings;
        widgets.Changed += () => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    public void SetSettings(AppSettings settings) { _settings = settings; Refresh(); }

    private void Refresh()
    {
        if (_widgets == null) return;
        if (IsVisible) _ = _widgets.RefreshAsync(); // TTL-gated: cheap when fresh
        _grid.Children.Clear();
        var now = DateTime.Now;
        var s = _settings;

        // Weather
        if (_widgets.TemperatureC is double t && _widgets.WeatherCode is int code)
        {
            var (glyph, text) = WidgetsService.DescribeWeather(code);
            double shown = s.WeatherFahrenheit ? t * 9 / 5 + 32 : t;
            Tile($"{glyph} {shown:0}°", s.WeatherCity ?? text, text);
        }
        else Tile("—", Loc.T("Weather"), string.IsNullOrWhiteSpace(s.WeatherCity) ? Loc.T("Set a city in settings") : Loc.T("Loading…"));

        // Air quality
        if (_widgets.UsAqi is int aqi) Tile(aqi.ToString(), Loc.T("Air · {0}", Loc.T(WidgetsService.AqiBand(aqi))), Loc.T("US AQI"), AqiColor(aqi));
        else Tile("—", Loc.T("Air"), Loc.T("Needs a city"));

        // Currency
        var pair = WidgetsService.ParsePair(s.CurrencyPair);
        Tile(_widgets.FxRate is double fx ? fx.ToString(fx >= 100 ? "0" : "0.###") : "—",
             pair is { } p ? Loc.T("1 {0} in {1}", p.From, p.To) : Loc.T("Currency"), Loc.T("European Central Bank rate"));

        // Ping
        Tile(_widgets.PingMs is long ms ? $"{ms} ms" : "lost", Loc.T("Ping"), s.PingHost, _widgets.Ping switch
        {
            PingQuality.Good => Color.FromRgb(0x30, 0xD1, 0x58),
            PingQuality.Fair => Color.FromRgb(0xFF, 0x9F, 0x0A),
            _ => Color.FromRgb(0xFF, 0x45, 0x3A),
        });

        // Screen time
        var st = _widgets.ScreenTimeToday;
        Tile(st.TotalHours >= 1 ? $"{(int)st.TotalHours}h {st.Minutes:00}m" : $"{st.Minutes}m", Loc.T("Screen time"), Loc.T("Active time today"));

        // Countdown
        Tile(WidgetsService.CountdownText(s.CountdownDate, now) ?? "—",
             string.IsNullOrWhiteSpace(s.CountdownTitle) ? Loc.T("Countdown") : s.CountdownTitle!,
             string.IsNullOrWhiteSpace(s.CountdownDate) ? Loc.T("Set a date in settings") : s.CountdownDate!);

        // World clock
        var zone = WidgetsService.FindZone(s.WorldClockZone);
        Tile(zone != null ? TimeZoneInfo.ConvertTime(now, zone).ToString("t") : "—",
             zone != null ? CityOf(zone) : Loc.T("World clock"), zone?.DisplayName ?? Loc.T("Set a city in settings"));

        // Focus (Pomodoro) — tap: start / pause / resume; right-click: reset
        if (_timer != null)
        {
            bool idle = _timer.State is CountdownTimerState.Idle or CountdownTimerState.Completed;
            var focus = Tile(idle ? Loc.T("{0}:00", s.FocusMinutes) : _timer.FormattedTime,
                 idle ? Loc.T("Focus") : _timer.State == CountdownTimerState.Paused ? Loc.T("{0} · paused", Loc.T(_timer.Label)) : Loc.T(_timer.Label),
                 Loc.T("Click to start, pause or resume · right-click to reset"),
                 _timer.State == CountdownTimerState.Running ? Color.FromRgb(0x0A, 0x84, 0xFF) : null);
            focus.MouseLeftButtonUp += (_, _) =>
            {
                if (idle) _timer.Start(TimeSpan.FromMinutes(Math.Max(1, s.FocusMinutes)), "Focus"); // logic key (Pomodoro compares it); shown via Loc.T
                else if (_timer.State == CountdownTimerState.Running) _timer.Pause();
                else _timer.Resume();
                Refresh();
            };
            focus.MouseRightButtonUp += (_, e) => { e.Handled = true; _timer.Reset(); Refresh(); };
        }

        // Pet — follows how long you've been away (rare, quiet delight)
        var action = WidgetsService.PetFor(WidgetsService.IdleTime(), now);
        var petGlyph = s.PetKind switch { PetKind.Dog => "🐕", PetKind.Bunny => "🐇", _ => "🐈" };
        var pet = Tile(action == PetAction.Sleep ? "💤" : petGlyph, action switch
        {
            PetAction.Sleep => Loc.T("Sleeping"), PetAction.Sit => Loc.T("Waiting for you"), PetAction.Look => Loc.T("Looking around"), _ => Loc.T("Walking"),
        }, Loc.T("Your idle pet"));
        Bob(pet, action == PetAction.Walk);
    }

    private Border Tile(string value, string label, string? tip, Color? accent = null)
    {
        var v = new TextBlock
        {
            Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = accent is Color c ? new SolidColorBrush(c) : (Brush)FindResource("TextPrimaryBrush"),
        };
        var l = new TextBlock
        {
            Text = label, FontSize = 9, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
        };
        var b = new Border
        {
            CornerRadius = new CornerRadius(10), Margin = new Thickness(2), Padding = new Thickness(8, 7, 6, 6),
            Background = (Brush)FindResource("SurfaceBrush"), ToolTip = tip, Cursor = Cursors.Arrow,
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { v, l } },
        };
        _grid.Children.Add(b);
        return b;
    }

    private void Bob(Border tile, bool walking)
    {
        var glyph = (TextBlock)((StackPanel)tile.Child).Children[0];
        glyph.RenderTransform = _petBob;
        if (!walking || !SystemParameters.ClientAreaAnimation) { _petBob.BeginAnimation(TranslateTransform.YProperty, null); return; }
        if (_petBob.HasAnimatedProperties) return; // keep the running cycle; the card rebuilds every second
        _petBob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -2.5, TimeSpan.FromMilliseconds(380))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private static Color AqiColor(int aqi) => aqi <= 50 ? Color.FromRgb(0x30, 0xD1, 0x58)
        : aqi <= 100 ? Color.FromRgb(0xFF, 0xD6, 0x0A) : aqi <= 150 ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Color.FromRgb(0xFF, 0x45, 0x3A);

    private static string CityOf(TimeZoneInfo z)
    {
        // "(UTC+09:00) Osaka, Sapporo, Tokyo" → "Tokyo"
        var name = z.DisplayName;
        int paren = name.IndexOf(')');
        var rest = paren >= 0 ? name[(paren + 1)..].Trim() : name;
        var parts = rest.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : z.Id;
    }
}
