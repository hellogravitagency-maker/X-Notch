namespace XNotch.Models;

/// <summary>
/// Central shape-shifting state machine (S0 through S7) for X-Notch Dynamic Island.
/// Preserves legacy enum values (Closed=0, Peeking=1, Open=2) for complete ABI compatibility.
/// </summary>
public enum NotchState
{
    /// <summary>S0 &amp; S1: Island dormant or compact ambient pill displaying minimal system indicators.</summary>
    Closed = 0,
    Dormant = 0,
    Ambient = 0,

    /// <summary>S2: Appears on intentional pointer hover; reveals contextual preview capsule.</summary>
    Peeking = 1,
    Peek = 1,

    /// <summary>S3: Full contextual interface with interactive controls and card carousel.</summary>
    Open = 2,
    Expanded = 2,

    /// <summary>S4: Pinned focus mode; remains visible while user works without auto-closing.</summary>
    Focus = 3,

    /// <summary>S5: Contextual event-driven presentation for notifications, charging, USB, and mode alerts.</summary>
    Notification = 4,

    /// <summary>S6: Raycast-inspired search and command interface with instant keyboard focus.</summary>
    Command = 5,

    /// <summary>S7: Expanded multi-widget productivity experience for tasks, agents, and system metrics.</summary>
    Workspace = 6
}

public static class NotchStateExtensions
{
    /// <summary>Whether the notch is in any expanded or interactive view.</summary>
    public static bool IsExpandedState(this NotchState state) =>
        state is NotchState.Open or NotchState.Focus or NotchState.Command or NotchState.Workspace;

    /// <summary>Whether the notch is in a resting compact state.</summary>
    public static bool IsRestingState(this NotchState state) =>
        state is NotchState.Closed;

    /// <summary>Whether the notch is peeking on hover.</summary>
    public static bool IsPeeking(this NotchState state) =>
        state is NotchState.Peeking;
}
