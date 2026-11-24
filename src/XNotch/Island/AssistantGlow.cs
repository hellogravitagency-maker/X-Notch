using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using FlowDirection = System.Windows.FlowDirection;
using ActivitySource = XNotch.Island.ActivitySource;

namespace XNotch.Island;

/// <summary>
/// The assistant (agent) glow: a conic gradient (#FF2D55 → #AF52DE → #0A84FF →
/// #5AC8FA → #FF2D55) rendered once into a cached bitmap and used as an ImageBrush
/// whose relative rotation turns 360° every 3 s. No shader, no per-frame raster work.
/// </summary>
public sealed class AssistantGlow
{
    private static BitmapSource? _bitmap;
    private readonly RotateTransform _rotation = new(0, 0.5, 0.5);

    public AssistantGlow()
    {
        Brush = new ImageBrush(Bitmap) { Stretch = Stretch.Fill, RelativeTransform = _rotation };
    }

    /// <summary>Use as Stroke for the 2 px ring and its blurred halo.</summary>
    public ImageBrush Brush { get; }

    /// <summary>Sets the rotation from elapsed seconds.</summary>
    public void SetTime(double seconds) =>
        _rotation.Angle = seconds / IslandMetrics.AssistantPeriodSeconds % 1.0 * 360.0;

    /// <summary>The cached conic gradient (built on first use).</summary>
    public static BitmapSource Bitmap => _bitmap ??= Render(IslandMetrics.AssistantBitmapSize);

    internal static BitmapSource Render(int size)
    {
        var stops = Array.ConvertAll(IslandColors.AssistantStops, h => (Color)ColorConverter.ConvertFromString(h));
        var px = new byte[size * size * 4];
        double c = (size - 1) / 2.0;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Angle from 12 o'clock, clockwise, 0..1
            double t = (Math.Atan2(x - c, c - y) / (2 * Math.PI) + 1) % 1;
            double f = t * (stops.Length - 1);
            int i = Math.Min((int)f, stops.Length - 2);
            double k = f - i;
            Color a = stops[i], b = stops[i + 1];
            int o = (y * size + x) * 4;
            px[o] = (byte)(a.B + (b.B - a.B) * k);
            px[o + 1] = (byte)(a.G + (b.G - a.G) * k);
            px[o + 2] = (byte)(a.R + (b.R - a.R) * k);
            px[o + 3] = 255;
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
        bmp.Freeze();
        return bmp;
    }
}
