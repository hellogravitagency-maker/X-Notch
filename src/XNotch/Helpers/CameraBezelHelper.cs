using System;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace XNotch.Helpers;

/// <summary>
/// Geometry and proximity helper for detecting cursor placement under the physical webcam bezel.
/// </summary>
public static class CameraBezelHelper
{
    public const int DefaultHorizontalRadiusPx = 135; // 270px wide trigger zone
    public const int DefaultVerticalDepthPx = 38;      // Top 38px of display bezel

    /// <summary>
    /// Determines whether the given physical cursor position is directly beneath the camera
    /// bezel at the top-center of the screen.
    /// </summary>
    public static bool IsInCameraZone(
        DrawingPoint cursorPos,
        DrawingRectangle screenPhysical,
        int horizontalRadiusPx = DefaultHorizontalRadiusPx,
        int verticalDepthPx = DefaultVerticalDepthPx)
    {
        if (!screenPhysical.Contains(cursorPos))
            return false;

        int screenCenterX = screenPhysical.Left + screenPhysical.Width / 2;
        int distX = Math.Abs(cursorPos.X - screenCenterX);
        int distY = cursorPos.Y - screenPhysical.Top;

        return distX <= horizontalRadiusPx && distY >= 0 && distY <= verticalDepthPx;
    }
}
