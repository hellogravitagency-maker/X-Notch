using System;
using System.Collections.Generic;
using System.Linq;

namespace XNotch.Island;

/// <summary>
/// FIFO of transient island alerts. One alert shows at a time for
/// <see cref="DisplayDuration"/>; hovering pauses the countdown; the same key
/// within <see cref="DedupeWindow"/> is dropped; at most <see cref="MaxQueued"/>
/// wait (oldest dropped). In game mode only low-battery alerts show and the
/// others are held for <see cref="GameModeHold"/> at most, then dropped.
/// Pure and clock-driven: call <see cref="Update"/> periodically.
/// </summary>
public sealed class AlertQueue
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(2.6);
    public static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan GameModeHold = TimeSpan.FromSeconds(10);
    public const int MaxQueued = 3;

    private readonly IClock _clock;
    private readonly LinkedList<(IslandAlert Alert, TimeSpan PostedAt)> _queue = new();
    private readonly Dictionary<string, TimeSpan> _lastPosted = new(StringComparer.Ordinal);
    private TimeSpan _deadline;
    private TimeSpan _pausedRemaining;
    private bool _hoverPaused;
    private bool _gameMode;

    public AlertQueue(IClock clock) => _clock = clock;

    /// <summary>The alert on screen, or null.</summary>
    public IslandAlert? Current { get; private set; }

    /// <summary>Alerts waiting behind <see cref="Current"/>.</summary>
    public int QueuedCount => _queue.Count;

    /// <summary>True while anything is showing or waiting (the caller keeps ticking).</summary>
    public bool IsBusy => Current != null || _queue.Count > 0;

    /// <summary>Fullscreen / game mode: suppress everything except low battery.</summary>
    public bool GameMode
    {
        get => _gameMode;
        set
        {
            if (_gameMode == value) return;
            _gameMode = value;
            if (value && Current is { IsLowBattery: false }) Current = null; // hidden behind the game anyway
            Update();
        }
    }

    /// <summary>Hovering the island pauses the dismissal countdown.</summary>
    public bool HoverPaused
    {
        get => _hoverPaused;
        set
        {
            if (_hoverPaused == value) return;
            _hoverPaused = value;
            var now = _clock.Now;
            if (value) _pausedRemaining = _deadline > now ? _deadline - now : TimeSpan.Zero;
            else _deadline = now + _pausedRemaining;
        }
    }

    /// <summary>Queues an alert. Returns false when it was deduped.</summary>
    public bool Post(IslandAlert alert)
    {
        var now = _clock.Now;
        if (_lastPosted.Count > 32) TrimDedupe();
        if (_lastPosted.TryGetValue(alert.Key, out var last) && now - last < DedupeWindow)
            return false;
        _lastPosted[alert.Key] = now;

        _queue.AddLast((alert, now));
        while (_queue.Count > MaxQueued) _queue.RemoveFirst();
        Update();
        return true;
    }

    /// <summary>Dismisses the current alert now (e.g. clicked).</summary>
    public void Dismiss()
    {
        Current = null;
        Update();
    }

    /// <summary>Advances timers. Returns true when <see cref="Current"/> changed.</summary>
    public bool Update()
    {
        var now = _clock.Now;
        var before = Current;

        if (Current != null && !_hoverPaused && now >= _deadline)
            Current = null;

        // Game mode: held alerts expire after GameModeHold.
        if (_gameMode)
        {
            for (var n = _queue.First; n != null;)
            {
                var next = n.Next;
                if (!n.Value.Alert.IsLowBattery && now - n.Value.PostedAt >= GameModeHold) _queue.Remove(n);
                n = next;
            }
        }

        if (Current == null)
        {
            var pick = _queue.FirstOrDefault(q => !_gameMode || q.Alert.IsLowBattery);
            if (pick.Alert != null)
            {
                _queue.Remove(pick);
                Current = pick.Alert;
                _deadline = now + DisplayDuration;
                _pausedRemaining = DisplayDuration;
            }
        }

        return !ReferenceEquals(before, Current);
    }

    /// <summary>Drops the debounce memory for keys older than the dedupe window (keeps the map small).</summary>
    internal void TrimDedupe()
    {
        var now = _clock.Now;
        foreach (var k in _lastPosted.Where(p => now - p.Value >= DedupeWindow).Select(p => p.Key).ToList())
            _lastPosted.Remove(k);
    }
}
