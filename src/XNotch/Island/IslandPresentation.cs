using System;
using System.Diagnostics;

namespace XNotch.Island;

/// <summary>
/// What the island is showing, layered on top of <see cref="Models.NotchState"/>
/// (which stays the interaction layer: closed / peeking / open).
/// </summary>
public enum IslandPresentation
{
    /// <summary>No live activities: a plain black pill.</summary>
    Idle,
    /// <summary>One live activity: leading slot + trailing slot.</summary>
    Compact,
    /// <summary>Two live activities: shortened pill + detached circle.</summary>
    Minimal,
    /// <summary>NotchState.Open: the widgets carousel, activity pages first.</summary>
    Expanded,
    /// <summary>Transient system notice (2.6 s), then back to the previous presentation.</summary>
    Alert,
    /// <summary>Ctrl+Space: the island is the search field (modal).</summary>
    Spotlight,
    /// <summary>Volume / brightness HUD, 1.5 s after the last change.</summary>
    Hud,
}

/// <summary>A transient island alert. <paramref name="Key"/> dedupes repeats.</summary>
/// <param name="Key">Identity for dedupe (same key within 1 s is dropped).</param>
/// <param name="Title">15/SemiBold line.</param>
/// <param name="Subtitle">13/Regular secondary line.</param>
/// <param name="Icon">A Segoe Fluent Icons glyph (or any short text).</param>
/// <param name="IsLowBattery">Low battery is the only alert shown during game mode.</param>
/// <param name="Value">Optional trailing number (e.g. battery %), rendered with rolling digits.</param>
/// <param name="AccentHex">Optional accent for the icon / trailing ring, e.g. "#FF453A".</param>
public sealed record IslandAlert(
    string Key,
    string Title,
    string? Subtitle = null,
    string? Icon = null,
    bool IsLowBattery = false,
    int? Value = null,
    string? AccentHex = null);

/// <summary>Monotonic time source, injectable for tests.</summary>
public interface IClock
{
    /// <summary>Monotonic time since an arbitrary origin.</summary>
    TimeSpan Now { get; }
}

/// <summary>Real clock backed by <see cref="Stopwatch"/>.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Shared instance.</summary>
    public static readonly SystemClock Instance = new();
    private static readonly long Origin = Stopwatch.GetTimestamp();

    /// <inheritdoc/>
    public TimeSpan Now => Stopwatch.GetElapsedTime(Origin);
}
