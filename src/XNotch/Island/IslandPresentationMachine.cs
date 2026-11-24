using System;
using System.Collections.Generic;
using System.Linq;
using XNotch.Models;

namespace XNotch.Island;

/// <summary>
/// Decides what the island presents. Pure and deterministic (no WPF types):
/// inputs are the live activities, the interaction state, the alert queue,
/// the HUD ping, Spotlight and game mode; outputs are the presentation and the
/// primary / secondary activity. Overlay order: Spotlight (modal) &gt; Hud &gt;
/// Alert &gt; base (Expanded / Minimal / Compact / Idle). Removing an overlay
/// falls back to the base, i.e. "returns to the previous presentation".
/// </summary>
public sealed class IslandPresentationMachine
{
    public static readonly TimeSpan HudDuration = TimeSpan.FromSeconds(1.5);

    private readonly IClock _clock;
    private List<ActivityEntry> _activities = [];
    private string? _pinnedPrimaryId;
    private bool _spotlight;
    private TimeSpan _hudUntil = TimeSpan.MinValue;

    public IslandPresentationMachine(IClock clock, AlertQueue? alerts = null)
    {
        _clock = clock;
        Alerts = alerts ?? new AlertQueue(clock);
        Recompute();
    }

    public AlertQueue Alerts { get; }
    public NotchState Interaction { get; private set; } = NotchState.Closed;
    public IslandPresentation Presentation { get; private set; } = IslandPresentation.Idle;
    /// <summary>The presentation without overlays (what Alert/Hud/Spotlight return to).</summary>
    public IslandPresentation BasePresentation { get; private set; } = IslandPresentation.Idle;
    public IReadOnlyList<ActivityEntry> Ordered { get; private set; } = [];
    public ActivityEntry? Primary => Ordered.Count > 0 ? Ordered[0] : null;
    public ActivityEntry? Secondary => Ordered.Count > 1 ? Ordered[1] : null;
    public bool IsPeeking => Interaction == NotchState.Peeking && Presentation is IslandPresentation.Idle or IslandPresentation.Compact or IslandPresentation.Minimal;
    public bool HudActive => _clock.Now < _hudUntil;
    public bool SpotlightOpen => _spotlight;

    /// <summary>Raised after any input changed the outputs.</summary>
    public event Action? Changed;

    public void SetActivities(IEnumerable<ActivityEntry> activities)
    {
        _activities = activities.ToList();
        Apply();
    }

    public void SetInteraction(NotchState state)
    {
        Interaction = state;
        Apply();
    }

    public void SetGameMode(bool on)
    {
        Alerts.GameMode = on;
        Apply();
    }

    public void PostAlert(IslandAlert alert)
    {
        Alerts.Post(alert);
        Apply();
    }

    public void OpenSpotlight() { _spotlight = true; Apply(); }
    public void CloseSpotlight() { _spotlight = false; Apply(); }

    /// <summary>A volume/brightness change: (re)starts the 1.5 s HUD window.</summary>
    public void PingHud()
    {
        _hudUntil = _clock.Now + HudDuration;
        Apply();
    }

    /// <summary>Minimal: the detached circle becomes primary (and back).</summary>
    public void SwapPrimary()
    {
        if (Secondary is { } s) { _pinnedPrimaryId = s.Id; Apply(); }
    }

    /// <summary>Advances the alert queue and HUD timer. Returns true when outputs changed.</summary>
    public bool Update()
    {
        Alerts.Update();
        return Apply();
    }

    private bool Apply()
    {
        var before = (Presentation, BasePresentation, Primary?.Id, Secondary?.Id);
        Recompute();
        bool changed = before != (Presentation, BasePresentation, Primary?.Id, Secondary?.Id);
        if (changed) Changed?.Invoke();
        return changed;
    }

    private void Recompute()
    {
        var ordered = ActivityPriorityResolver.Order(_activities).ToList();

        // A manual swap holds only while both activities are still the top two.
        if (_pinnedPrimaryId != null)
        {
            int i = ordered.FindIndex(a => a.Id == _pinnedPrimaryId);
            if (i is 0 or 1)
            {
                var pinned = ordered[i];
                ordered.RemoveAt(i);
                ordered.Insert(0, pinned);
            }
            else _pinnedPrimaryId = null;
        }
        Ordered = ordered;

        BasePresentation = Interaction == NotchState.Open ? IslandPresentation.Expanded
            : ordered.Count switch
            {
                0 => IslandPresentation.Idle,
                1 => IslandPresentation.Compact,
                _ => IslandPresentation.Minimal,
            };

        Presentation = _spotlight ? IslandPresentation.Spotlight
            : HudActive ? IslandPresentation.Hud
            : Alerts.Current != null ? IslandPresentation.Alert
            : BasePresentation;
    }
}
