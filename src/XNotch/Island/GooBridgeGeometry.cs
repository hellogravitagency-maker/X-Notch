using System;
using System.Windows;
using System.Windows.Media;

namespace XNotch.Island;

/// <summary>
/// Metaball connector between two circles (Hiroyuki Sato's algorithm, as popularised
/// for SVG by Varun Vachhar), used for the gooey Minimal split / merge since WPF has
/// no goo filter. The neck exists while the centre distance is within
/// r1 + r2 + <see cref="IslandMetrics.GooBreakSlack"/>, then snaps off.
/// </summary>
public static class GooBridgeGeometry
{
    private const double HalfPi = Math.PI / 2;

    /// <summary>Distance at which the neck breaks.</summary>
    public static double BreakDistance(double r1, double r2) => r1 + r2 + IslandMetrics.GooBreakSlack;

    /// <summary>
    /// The neck between two circles, or null when it is broken (too far) or not needed
    /// (one circle contains the other).
    /// </summary>
    public static Geometry? CreateBridge(Point c1, double r1, Point c2, double r2,
        double v = IslandMetrics.GooSpread, double handleSize = IslandMetrics.GooHandle)
    {
        double d = (c2 - c1).Length;
        if (r1 <= 0 || r2 <= 0 || d > BreakDistance(r1, r2) || d <= Math.Abs(r1 - r2) + 1e-6)
            return null;

        double u1 = 0, u2 = 0;
        if (d < r1 + r2)
        {
            u1 = Math.Acos(Math.Clamp((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d), -1, 1));
            u2 = Math.Acos(Math.Clamp((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d), -1, 1));
        }

        double between = Math.Atan2(c2.Y - c1.Y, c2.X - c1.X);
        double maxSpread = Math.Acos(Math.Clamp((r1 - r2) / d, -1, 1));

        double a1 = between + u1 + (maxSpread - u1) * v;
        double a2 = between - u1 - (maxSpread - u1) * v;
        double a3 = between + Math.PI - u2 - (Math.PI - u2 - maxSpread) * v;
        double a4 = between - Math.PI + u2 + (Math.PI - u2 - maxSpread) * v;

        Point p1 = Polar(c1, a1, r1), p2 = Polar(c1, a2, r1);
        Point p3 = Polar(c2, a3, r2), p4 = Polar(c2, a4, r2);

        double total = r1 + r2;
        double d2 = Math.Min(v * handleSize, (p1 - p3).Length / total) * Math.Min(1, d * 2 / total);
        double h1Len = r1 * d2, h2Len = r2 * d2;

        Point h1 = Polar(p1, a1 - HalfPi, h1Len);
        Point h2 = Polar(p2, a2 + HalfPi, h1Len);
        Point h3 = Polar(p3, a3 + HalfPi, h2Len);
        Point h4 = Polar(p4, a4 - HalfPi, h2Len);

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p1, isFilled: true, isClosed: true);
            ctx.BezierTo(h1, h3, p3, true, true);
            ctx.ArcTo(p4, new Size(r2, r2), 0, d > r1, SweepDirection.Counterclockwise, true, true); // SVG sweep-flag 0
            ctx.BezierTo(h4, h2, p2, true, true);
            // Close across circle 1 (inside the pill / circle, so the chord is hidden).
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Pill + detached circle + neck as one filled shape. The neck joins the pill's
    /// trailing end cap (a circle of the pill's radius) to the detached circle.
    /// </summary>
    public static Geometry CreateMinimal(Geometry pill, Rect pillRect, Point circleCenter, double circleRadius)
    {
        double capR = pillRect.Height / 2;
        var capCenter = new Point(pillRect.Right - capR, pillRect.Top + capR);
        var circle = new EllipseGeometry(circleCenter, circleRadius, circleRadius);

        Geometry shape = new CombinedGeometry(GeometryCombineMode.Union, pill, circle);
        var bridge = CreateBridge(capCenter, capR, circleCenter, circleRadius);
        if (bridge != null) shape = new CombinedGeometry(GeometryCombineMode.Union, shape, bridge);
        shape.Freeze();
        return shape;
    }

    private static Point Polar(Point c, double angle, double r) =>
        new(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));
}
