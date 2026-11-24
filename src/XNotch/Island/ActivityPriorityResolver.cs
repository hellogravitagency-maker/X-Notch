using System;
using System.Collections.Generic;
using System.Linq;

namespace XNotch.Island;

/// <summary>Where a live activity comes from (plugin priorities are clamped).</summary>
public enum ActivitySource { BuiltIn, Plugin }

/// <summary>Pure snapshot of one live activity, used for ordering.</summary>
public readonly record struct ActivityEntry(
    string Id,
    int Priority,
    DateTime StartedAt,
    ActivitySource Source = ActivitySource.BuiltIn);

/// <summary>Built-in live activity priorities (higher wins the compact pill).</summary>
public static class ActivityPriority
{
    public const int Agent = 90;
    public const int Timer = 70;
    public const int Stopwatch = 60;
    public const int Media = 50;
    public const int PluginDefault = 40;
    public const int PluginMin = 0;
    public const int PluginMax = 80;
    public const int Shelf = 30;
}

/// <summary>
/// Orders live activities: highest priority first, ties broken by most recently started.
/// Index 0 = primary (compact pill), 1 = secondary (minimal circle), the rest are queued
/// and promote automatically when one ends (the caller just re-resolves).
/// </summary>
public static class ActivityPriorityResolver
{
    /// <summary>Plugin priorities are clamped to 0–80 so they can never outrank an agent.</summary>
    public static int EffectivePriority(ActivityEntry e) =>
        e.Source == ActivitySource.Plugin
            ? Math.Clamp(e.Priority, ActivityPriority.PluginMin, ActivityPriority.PluginMax)
            : e.Priority;

    /// <summary>Returns the activities in display order.</summary>
    public static IReadOnlyList<ActivityEntry> Order(IEnumerable<ActivityEntry> active) =>
        active
            .OrderByDescending(EffectivePriority)
            .ThenByDescending(e => e.StartedAt)
            .ThenBy(e => e.Id, StringComparer.Ordinal) // deterministic last resort
            .ToList();
}
