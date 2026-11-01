using System;
using System.Windows.Threading;

namespace XNotch.Services;

public enum CountdownTimerState
{
    Idle,
    Running,
    Paused,
    Completed
}

/// <summary>
/// Provides live focus / countdown timer functionality with Apple Dynamic Island compact
/// ring display, pause/resume, and expiration chime/alerts.
/// </summary>
public sealed class CountdownTimerService : IDisposable
{
    private DispatcherTimer? _timer;
    private DateTime _endTime;
    private TimeSpan _remaining;
    private TimeSpan _totalDuration;
    private bool _disposed;

    public CountdownTimerState State { get; private set; } = CountdownTimerState.Idle;
    public TimeSpan RemainingTime => _remaining;
    public TimeSpan TotalDuration => _totalDuration;
    public double Progress => _totalDuration.TotalSeconds > 0 
        ? Math.Clamp(1.0 - (_remaining.TotalSeconds / _totalDuration.TotalSeconds), 0.0, 1.0) 
        : 0.0;

    public string FormattedTime => _remaining.TotalHours >= 1 
        ? _remaining.ToString(@"h\:mm\:ss") 
        : _remaining.ToString(@"m\:ss");

    public string Label { get; private set; } = "Focus";

    public event Action<TimeSpan, double>? Tick;
    public event Action? Completed;
    public event Action<CountdownTimerState>? StateChanged;

    public void Initialize()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start(TimeSpan duration, string label = "Focus")
    {
        if (duration <= TimeSpan.Zero) return;

        _totalDuration = duration;
        _remaining = duration;
        _endTime = DateTime.UtcNow.Add(duration);
        Label = label;
        State = CountdownTimerState.Running;

        _timer?.Start();
        StateChanged?.Invoke(State);
        Tick?.Invoke(_remaining, Progress);
    }

    public void Pause()
    {
        if (State != CountdownTimerState.Running) return;

        _timer?.Stop();
        State = CountdownTimerState.Paused;
        StateChanged?.Invoke(State);
    }

    public void Resume()
    {
        if (State != CountdownTimerState.Paused) return;

        _endTime = DateTime.UtcNow.Add(_remaining);
        State = CountdownTimerState.Running;
        _timer?.Start();
        StateChanged?.Invoke(State);
    }

    public void Reset()
    {
        _timer?.Stop();
        _remaining = TimeSpan.Zero;
        _totalDuration = TimeSpan.Zero;
        State = CountdownTimerState.Idle;
        StateChanged?.Invoke(State);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (State != CountdownTimerState.Running) return;

        var now = DateTime.UtcNow;
        if (now >= _endTime)
        {
            _remaining = TimeSpan.Zero;
            _timer?.Stop();
            State = CountdownTimerState.Completed;
            StateChanged?.Invoke(State);
            Completed?.Invoke();
            try
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
            catch { }
            return;
        }

        _remaining = _endTime - now;
        Tick?.Invoke(_remaining, Progress);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
    }
}
