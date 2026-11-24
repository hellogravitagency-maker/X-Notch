using System.Collections.Generic;
using XNotch.Helpers;

namespace XNotch.Island;

/// <summary>Response (s) + damping ratio, the <see cref="SpringAnimator"/> model.</summary>
public readonly record struct SpringPreset(double Response, double Damping);

/// <summary>
/// Every island spring preset. The first three are the pre-existing
/// expand / collapse / peek values, unchanged. <see cref="Overrides"/> is the one
/// place a future plugin mechanism can replace presets by name; no plugin
/// overrides presets today (IAnimationPlugin is Storyboard-based and unused).
/// </summary>
public static class IslandSprings
{
    public static readonly SpringPreset Expand = new(0.38, 0.78);
    public static readonly SpringPreset Collapse = new(0.32, 0.88);
    public static readonly SpringPreset Peek = new(0.28, 0.72);

    /// <summary>≈4 % overshoot, visible on purpose.</summary>
    public static readonly SpringPreset ExpandActivity = new(0.40, 0.72);
    public static readonly SpringPreset AlertIn = new(0.42, 0.70);
    public static readonly SpringPreset ContentIn = new(0.30, 0.85);
    public static readonly SpringPreset GooSplit = new(0.50, 0.62);
    public static readonly SpringPreset DigitRoll = new(0.24, 0.90);
    public static readonly SpringPreset SquashRecover = new(0.25, 0.60);

    /// <summary>Name → replacement preset (e.g. "ExpandActivity").</summary>
    public static Dictionary<string, SpringPreset> Overrides { get; } = new();

    /// <summary>Returns the override for <paramref name="name"/> or the default.</summary>
    public static SpringPreset Resolve(string name, SpringPreset fallback) =>
        Overrides.TryGetValue(name, out var p) ? p : fallback;

    /// <summary>Configures a spring from a preset.</summary>
    public static SpringAnimator Use(this SpringAnimator spring, SpringPreset preset)
    {
        spring.SetPhysics(preset.Response, preset.Damping);
        return spring;
    }
}
