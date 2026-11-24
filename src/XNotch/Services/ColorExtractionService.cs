using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XNotch.Services;

public static class ColorExtractionService
{
    /// <summary>
    /// Turns a raw dominant colour into a usable glow tint: keeps the hue, forces
    /// saturation ≥ 0.6 and lightness into 0.55–0.68 so dark or washed-out art still glows.
    /// Greys (no hue) fall back to a soft white.
    /// </summary>
    public static Color ToGlowTint(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d < 0.04) return Color.FromRgb(0xE8, 0xE8, 0xF0);

        double h = max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        double l = (max + min) / 2;
        double s = d / (1 - Math.Abs(2 * l - 1));
        s = Math.Max(s, 0.6);
        l = Math.Clamp(l, 0.55, 0.68);

        // HSL → RGB
        double C = (1 - Math.Abs(2 * l - 1)) * s, X = C * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - C / 2;
        (double r1, double g1, double b1) = h switch
        {
            < 60 => (C, X, 0.0), < 120 => (X, C, 0.0), < 180 => (0.0, C, X),
            < 240 => (0.0, X, C), < 300 => (X, 0.0, C), _ => (C, 0.0, X),
        };
        return Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    /// <summary>
    /// Extracts the dominant color from a BitmapImage using a simple
    /// histogram-based approach on a downsampled copy.
    /// </summary>
    public static Color GetDominantColor(BitmapImage source)
    {
        try
        {
            // Downsample to 40x40 for speed
            const int sampleSize = 40;
            var resized = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var scaled = new TransformedBitmap(resized,
                new ScaleTransform(
                    sampleSize / (double)source.PixelWidth,
                    sampleSize / (double)source.PixelHeight));

            int width = scaled.PixelWidth;
            int height = scaled.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            scaled.CopyPixels(pixels, stride, 0);

            long totalR = 0, totalG = 0, totalB = 0;
            int count = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a < 128) continue;

                // Skip very dark and very bright pixels (they're not interesting)
                int brightness = (r + g + b) / 3;
                if (brightness < 30 || brightness > 240) continue;

                // Boost saturation weight: prefer colorful pixels
                int max = Math.Max(r, Math.Max(g, b));
                int min = Math.Min(r, Math.Min(g, b));
                int saturation = max - min;
                int weight = Math.Max(1, saturation);

                totalR += r * weight;
                totalG += g * weight;
                totalB += b * weight;
                count += weight;
            }

            if (count == 0)
                return Colors.DimGray;

            return Color.FromRgb(
                (byte)(totalR / count),
                (byte)(totalG / count),
                (byte)(totalB / count));
        }
        catch
        {
            return Colors.DimGray;
        }
    }
}
