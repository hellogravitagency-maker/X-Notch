using System.Collections.Generic;
using XNotch.Island.Activities;

namespace XNotch.Plugins;

/// <summary>
/// A plugin that publishes Dynamic Island live activities (compact pill, minimal
/// circle, expanded carousel page). Additive to the other plugin interfaces; a
/// plugin can implement this alongside <see cref="IUIPlugin"/> and friends.
/// </summary>
/// <remarks>
/// Activities are queried when the plugin is enabled (and again when plugins
/// change). Raise <see cref="ILiveActivity.Changed"/> whenever IsActive or the
/// content changes. Priorities are clamped to 0–80 (default 40), so a plugin can
/// never outrank a running agent (90). Requires X-Notch 0.9.0 or newer.
/// </remarks>
public interface ILiveActivityPlugin : IPlugin
{
    /// <summary>The activities this plugin owns (ids must be unique; prefix with the plugin id).</summary>
    IEnumerable<ILiveActivity> GetLiveActivities();
}

/// <summary>
/// Posts transient island alerts (2.6 s, queued, deduped by key within 1 s).
/// Get it with <c>Context.GetService&lt;IIslandAlerts&gt;()</c>. In Classic mode or
/// on a side dock the alert falls back to a regular X-Notch notification.
/// </summary>
public interface IIslandAlerts
{
    /// <param name="title">15/SemiBold line.</param>
    /// <param name="subtitle">Optional 13/Regular secondary line.</param>
    /// <param name="icon">Optional Segoe Fluent Icons glyph or short text / emoji.</param>
    /// <param name="key">Dedupe key (defaults to the title).</param>
    void Post(string title, string? subtitle = null, string? icon = null, string? key = null);
}
