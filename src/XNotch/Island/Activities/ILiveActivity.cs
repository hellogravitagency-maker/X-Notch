using System;
using System.Windows;

namespace XNotch.Island.Activities;

/// <summary>
/// A live activity shown in the Dynamic Island. Built-in activities (music, timer,
/// stopwatch, agent, shelf) and plugins (ILiveActivityPlugin)
/// implement this. Views are created on the UI thread when needed and may be
/// requested again; <see cref="Changed"/> may fire from any thread.
/// </summary>
public interface ILiveActivity
{
    /// <summary>Stable id, unique across activities (plugins: prefix with your plugin id).</summary>
    string Id { get; }

    /// <summary>
    /// Higher wins the compact pill. Built-ins: agent 90, timer 70, stopwatch 60,
    /// media 50, shelf 30. Plugin priorities are clamped to 0–80 (default 40).
    /// </summary>
    int Priority => ActivityPriority.PluginDefault;

    /// <summary>When the activity last became active (ties: most recent wins).</summary>
    DateTime StartedAt { get; }

    /// <summary>Whether the activity is live right now.</summary>
    bool IsActive { get; }

    /// <summary>Raised when <see cref="IsActive"/> or the content changes.</summary>
    event Action? Changed;

    /// <summary>Compact leading slot (22 px, left inset 6).</summary>
    FrameworkElement? CreateCompactLeading();

    /// <summary>Compact trailing slot (right inset 10).</summary>
    FrameworkElement? CreateCompactTrailing();

    /// <summary>Content of the detached 32 px circle when this is the secondary activity.</summary>
    FrameworkElement? CreateMinimalView() => CreateCompactLeading();

    /// <summary>Expanded carousel page (inserted first), or null for none.</summary>
    FrameworkElement? CreateExpandedView() => null;
}
