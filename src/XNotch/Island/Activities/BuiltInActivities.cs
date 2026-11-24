using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XNotch.Controls;
using XNotch.Models;
using XNotch.Services;

using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using FlowDirection = System.Windows.FlowDirection;
using ActivitySource = XNotch.Island.ActivitySource;

namespace XNotch.Island.Activities;

/// <summary>Shared start-time / change plumbing for the built-in activities.</summary>
public abstract class LiveActivityBase : ILiveActivity
{
    private bool _wasActive;

    public abstract string Id { get; }
    public abstract int Priority { get; }
    public DateTime StartedAt { get; private set; } = DateTime.Now;
    public abstract bool IsActive { get; }
    public event Action? Changed;

    /// <summary>Call when the source changed; stamps StartedAt on activation.</summary>
    protected void Raise()
    {
        bool active = IsActive;
        if (active && !_wasActive) StartedAt = DateTime.Now;
        _wasActive = active;
        Changed?.Invoke();
    }

    public abstract FrameworkElement? CreateCompactLeading();
    public abstract FrameworkElement? CreateCompactTrailing();
    public virtual FrameworkElement? CreateMinimalView() => CreateCompactLeading();
    public virtual FrameworkElement? CreateExpandedView() => null;
}

/// <summary>Now playing (SMTC): album art + art-tinted 4-bar spectrum.</summary>
public sealed class MusicActivity : LiveActivityBase
{
    private readonly MediaService _media;
    private readonly AudioCaptureService _audio;
    private readonly Func<bool> _enabled;

    public MusicActivity(MediaService media, AudioCaptureService audio, Func<bool> enabled)
    {
        _media = media;
        _audio = audio;
        _enabled = enabled;
        _media.MediaInfo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MediaInfo.HasMedia) or nameof(MediaInfo.IsPlaying)) Raise();
        };
        _media.SessionChanged += Raise;
    }

    public override string Id => "media";
    public override int Priority => ActivityPriority.Media;
    public override bool IsActive => _enabled() && _media.MediaInfo.HasMedia && _media.MediaInfo.IsPlaying;

    public override FrameworkElement CreateCompactLeading()
    {
        var art = IslandVisuals.Art(IslandMetrics.LeadingSlot, IslandMetrics.CompactArtRadius, _media.MediaInfo.AlbumArt);
        Follow(nameof(MediaInfo.AlbumArt), art, () =>
            art.Background = _media.MediaInfo.AlbumArt is { } a ? new ImageBrush(a) { Stretch = Stretch.UniformToFill } : IslandVisuals.Brush("#33FFFFFF"));
        return art;
    }

    public override FrameworkElement CreateCompactTrailing()
    {
        var bars = new WaveformBars { Tint = Services.ColorExtractionService.ToGlowTint(_media.MediaInfo.DominantColor) };
        void OnSpectrum() => bars.Dispatcher.BeginInvoke(() => bars.Update(_audio.SpectrumData, _media.MediaInfo.IsPlaying), DispatcherPriority.Render);
        bars.Loaded += (_, _) => _audio.SpectrumUpdated += OnSpectrum;
        bars.Unloaded += (_, _) => _audio.SpectrumUpdated -= OnSpectrum;
        Follow(nameof(MediaInfo.DominantColor), bars, () => bars.Tint = Services.ColorExtractionService.ToGlowTint(_media.MediaInfo.DominantColor));
        return bars;
    }

    public override FrameworkElement CreateMinimalView()
    {
        var art = IslandVisuals.Art(IslandMetrics.LeadingSlot, IslandMetrics.LeadingSlot / 2, _media.MediaInfo.AlbumArt);
        art.HorizontalAlignment = HorizontalAlignment.Center;
        return art;
    }

    /// <summary>Applies a property change to a view while it is loaded.</summary>
    private void Follow(string property, FrameworkElement view, Action apply)
    {
        PropertyChangedEventHandler h = (_, e) => { if (e.PropertyName == property) view.Dispatcher.BeginInvoke(apply); };
        view.Loaded += (_, _) => _media.MediaInfo.PropertyChanged += h;
        view.Unloaded += (_, _) => _media.MediaInfo.PropertyChanged -= h;
    }
}

/// <summary>Countdown timer: orange ring + rolling remaining time.</summary>
public sealed class TimerActivity : LiveActivityBase
{
    private readonly CountdownTimerService _timer;
    private ProgressRing? _lastRing;

    public TimerActivity(CountdownTimerService timer)
    {
        _timer = timer;
        _timer.StateChanged += _ => Raise();
    }

    public override string Id => "timer";
    public override int Priority => ActivityPriority.Timer;
    public override bool IsActive => _timer.State is CountdownTimerState.Running or CountdownTimerState.Paused;

    public override FrameworkElement CreateCompactLeading() => Ring();

    public override FrameworkElement CreateCompactTrailing()
    {
        var text = new RollingNumberText(IslandMetrics.NumberFontSize, FontWeights.SemiBold, IslandVisuals.Brush(IslandColors.Timer))
        { Text = RollingDigits.FormatClock(_timer.RemainingTime) };
        Action<TimeSpan, double> tick = (rem, _) => text.Dispatcher.BeginInvoke(() => text.Text = RollingDigits.FormatClock(rem));
        text.Loaded += (_, _) => _timer.Tick += tick;
        text.Unloaded += (_, _) => _timer.Tick -= tick;
        return text;
    }

    public override FrameworkElement CreateMinimalView() => Ring();

    /// <summary>Expanded page: big ring, label, rolling time, pause / stop.</summary>
    public override FrameworkElement CreateExpandedView()
    {
        var ring = new ProgressRing(IslandMetrics.ExpandedArt, IslandColors.Timer, 4) { Progress = 1 - _timer.Progress };
        var time = new RollingNumberText(IslandMetrics.NumberFontSize, FontWeights.SemiBold, IslandVisuals.Brush(IslandColors.Timer))
        { Text = RollingDigits.FormatClock(_timer.RemainingTime), HorizontalAlignment = HorizontalAlignment.Left };
        var pause = IslandVisuals.Glyph(_timer.State == CountdownTimerState.Paused ? "" : "", 16, IslandColors.Timer);
        var pauseButton = IslandVisuals.CircleButton("", IslandColors.Timer, () =>
        {
            if (_timer.State == CountdownTimerState.Running) _timer.Pause(); else _timer.Resume();
        });
        pauseButton.Child = pause;
        var stop = IslandVisuals.CircleButton("", IslandColors.Destructive, _timer.Reset);

        Action<TimeSpan, double> tick = (rem, p) => time.Dispatcher.BeginInvoke(() => { time.Text = RollingDigits.FormatClock(rem); ring.Progress = 1 - p; });
        Action<CountdownTimerState> state = s => pause.Dispatcher.BeginInvoke(() => pause.Text = s == CountdownTimerState.Paused ? "" : "");

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(IslandMetrics.ExpandedPadding, 0, 0, 0) };
        info.Children.Add(IslandVisuals.Text(_timer.Label, IslandMetrics.TitleFontSize));
        info.Children.Add(time);

        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(ring);
        Grid.SetColumn(info, 1); g.Children.Add(info);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { pauseButton, stop } };
        Grid.SetColumn(buttons, 2); g.Children.Add(buttons);

        g.Loaded += (_, _) => { _timer.Tick += tick; _timer.StateChanged += state; };
        g.Unloaded += (_, _) => { _timer.Tick -= tick; _timer.StateChanged -= state; };
        return g;
    }

    /// <summary>Pulses the ring currently on screen (timer done).</summary>
    public void Pulse() => _lastRing?.Pulse();

    private ProgressRing Ring()
    {
        var ring = new ProgressRing(IslandMetrics.TimerRing, IslandColors.Timer) { Progress = 1 - _timer.Progress, HorizontalAlignment = HorizontalAlignment.Center };
        Action<TimeSpan, double> tick = (_, p) => ring.Dispatcher.BeginInvoke(() => ring.Progress = 1 - p);
        ring.Loaded += (_, _) => { _timer.Tick += tick; _lastRing = ring; };
        ring.Unloaded += (_, _) => _timer.Tick -= tick;
        return ring;
    }
}

/// <summary>Stopwatch: glyph + rolling elapsed (while running or paused with time on it).</summary>
public sealed class StopwatchActivity : LiveActivityBase
{
    private readonly Views.StopwatchWidgetView _view;

    public StopwatchActivity(Views.StopwatchWidgetView view)
    {
        _view = view;
        _view.RunningChanged += Raise;
    }

    public override string Id => "stopwatch";
    public override int Priority => ActivityPriority.Stopwatch;
    public override bool IsActive => _view.IsRunning || _view.Elapsed > TimeSpan.Zero;

    public override FrameworkElement CreateCompactLeading() => IslandVisuals.Glyph("", 15, IslandColors.Timer);

    public override FrameworkElement CreateCompactTrailing()
    {
        var text = new RollingNumberText { Text = RollingDigits.FormatClock(_view.Elapsed) };
        // ponytail: 4 Hz poll of a UI-thread Stopwatch, only while this view is on screen
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        poll.Tick += (_, _) => text.Text = RollingDigits.FormatClock(_view.Elapsed);
        text.Loaded += (_, _) => poll.Start();
        text.Unloaded += (_, _) => poll.Stop();
        return text;
    }
}

/// <summary>A coding agent is working: sparkle + "Thinking…" (the window adds the glow ring).</summary>
public sealed class AgentActivity : LiveActivityBase
{
    private readonly AgentHubService _agents;

    public AgentActivity(AgentHubService agents)
    {
        _agents = agents;
        _agents.SessionsChanged += Raise;
    }

    public override string Id => "agent";
    public override int Priority => ActivityPriority.Agent;
    public override bool IsActive => _agents.Sessions.Any(s => s.State == AgentState.Working);

    public override FrameworkElement CreateCompactLeading() => IslandVisuals.Glyph("", 15, "#AF52DE");

    public override FrameworkElement CreateCompactTrailing() =>
        IslandVisuals.Text(Helpers.Loc.T("Thinking…"), IslandMetrics.CompactFontSize, bold: true, secondary: true);
}

/// <summary>Files are held on the Shelf: stacked file glyph + count.</summary>
public sealed class ShelfActivity : LiveActivityBase
{
    private readonly ShelfService _shelf;

    public ShelfActivity(ShelfService shelf)
    {
        _shelf = shelf;
        _shelf.ShelfChanged += Raise;
    }

    public override string Id => "shelf";
    public override int Priority => ActivityPriority.Shelf;
    public override bool IsActive => _shelf.HasItems;

    public override FrameworkElement CreateCompactLeading() => IslandVisuals.Glyph("", 15);

    public override FrameworkElement CreateCompactTrailing()
    {
        var text = new RollingNumberText { Text = _shelf.Items.Count.ToString() };
        Action update = () => text.Dispatcher.BeginInvoke(() => text.Text = _shelf.Items.Count.ToString());
        text.Loaded += (_, _) => _shelf.ShelfChanged += update;
        text.Unloaded += (_, _) => _shelf.ShelfChanged -= update;
        return text;
    }
}
