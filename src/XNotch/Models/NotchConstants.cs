using System.Windows;
using System.Text.Json.Serialization;

namespace XNotch.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotchDock
{
    Top,
    Left,
    Right
}

public static class NotchConstants
{
    // Closed / S1 Ambient dimensions (before DPI scaling)
    public const double ClosedWidth = 200;
    public const double ClosedHeight = 34;
    public const double AmbientWidth = 200;
    public const double AmbientHeight = 34;

    // S0 Auto-hide tucked dimension (subtle sliver at screen edge)
    public const double AutoHideHeight = 4.0;
    public const double DormantHeight = 4.0;

    // S2 Peek dimensions (slightly expanded on hover)
    public const double PeekWidth = 220;
    public const double PeekHeight = 34;

    // Audio capsule peek (compact interactive media controls on hover)
    public const double AudioPeekWidth = 295;
    public const double AudioPeekHeight = 36;

    // S4 Focus mode dimensions
    public const double FocusWidth = 440;
    public const double FocusHeight = 150;

    // S5 Notification capsule dimensions
    public const double NotificationWidth = 330;
    public const double NotificationHeight = 42;

    // S6 Command Center dimensions (Raycast-inspired)
    public const double CommandWidth = 520;
    public const double CommandHeight = 280;

    // S7 Workspace dimensions (large multi-widget productivity)
    public const double WorkspaceWidth = 560;
    public const double WorkspaceHeight = 420;

    // Shelf drop capsule dimensions (elastic pull-down when dragging files over notch)
    public const double ShelfDropWidth = 320;
    public const double ShelfDropHeight = 88;

    // HUD dimensions (wider for volume/brightness slider)
    public const double HudWidth = 320;

    // S3 Open dimensions (sized to fit music player & carousel content comfortably)
    public const double OpenWidth = 520;
    public const double OpenWidthWithCalendar = 755;
    public const double OpenWidthWithWebcam = 670;
    public const double OpenWidthWithCalendarAndWebcam = 905;
    public const double OpenHeight = 210;
    public const double OpenSettingsHeight = 440;

    // Corner radii
    public const double ClosedTopRadius = 6;
    public const double ClosedBottomRadius = 14;
    public const double PeekTopRadius = 8;
    public const double PeekBottomRadius = 16;
    public const double OpenTopRadius = 10;
    public const double OpenBottomRadius = 20;

    // Window padding around notch for shadow space
    public const double WindowPadding = 20;

    // Animation physics (Apple SwiftUI interactiveSpring equivalent)
    public const double DefaultSpringResponse = 0.40;
    public const double DefaultSpringDamping = 0.82;

    public const double PeekSpringResponse = 0.32;
    public const double PeekSpringDamping = 0.78;     // Squishy overshoot for tactile hover

    public const double OpenSpringResponse = 0.40;
    public const double OpenSpringDamping = 0.82;     // Fluid, natural expansion

    public const double CloseSpringResponse = 0.38;
    public const double CloseSpringDamping = 0.86;    // Snappy, clean settle into bezel

    public const double ShelfStretchResponse = 0.34;
    public const double ShelfStretchDamping = 0.76;   // Liquid rubber stretch during drag-drop

    // Hover
    public const int HoverOpenDelayMs = 200;
    public const int HoverCloseDelayMs = 150;

    // File Shelf
    public const double ShelfRowHeight = 80;
    public const double OpenHeightWithShelf = 300;
    public const double OpenSettingsHeightWithShelf = 520;

    // Shadow
    public static readonly System.Windows.Media.Color ShadowColor = System.Windows.Media.Color.FromArgb(180, 0, 0, 0);
    public const double ShadowBlurRadius = 14;
    public const double ShadowDepth = 2;

    // Side dock vertical open dimensions
    public const double SideOpenWidth = 160;    // how far the notch protrudes from edge
    public const double SideOpenHeight = 460;   // tall axis along screen edge

    // Drag & Dock
    public const double DragThreshold = 3;        // pixels before drag starts
    public const double EdgeSnapDistance = 80;     // px from screen edge to snap
    public const double ThrowVelocityThreshold = 800; // px/s to register as throw
}
