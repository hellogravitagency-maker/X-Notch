using System;
using System.Windows.Media;

namespace XNotch.Helpers;

public static class ColorHelper
{
    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            string clean = hex.Trim();
            if (!clean.StartsWith('#') && !clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (clean.Length == 6 || clean.Length == 8)
                    clean = "#" + clean;
            }
            var obj = System.Windows.Media.ColorConverter.ConvertFromString(clean);
            if (obj is Color c) return c;
            return fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static string ToHexString(Color color, bool includeAlpha = false)
    {
        return includeAlpha
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
