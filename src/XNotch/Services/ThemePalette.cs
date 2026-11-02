using System;
using System.Windows;
using System.Windows.Media;
using Windows.UI.ViewManagement;

namespace XNotch.Services;

public sealed class ThemePalette
{
    private const double MinimumTextContrast = 4.5;
    private const double MinimumAccentContrast = 3.0;

    public bool IsLight { get; init; }
    public bool IsHighContrast { get; init; }
    public bool UseReducedMotion { get; init; }
    public bool UseLiquidGlass { get; init; }

    public Color Background { get; init; }
    public Color Surface { get; init; }
    public Color SurfaceRaised { get; init; }
    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextTertiary { get; init; }
    public Color TextMuted { get; init; }
    public Color TextDim { get; init; }
    public Color TextSubtle { get; init; }
    public Color TextFaint { get; init; }
    public Color TextFaintest { get; init; }
    public Color Border { get; init; }
    public Color Accent { get; init; }
    public Color AccentPressed { get; init; }
    public Color AccentSoft { get; init; }
    public Color AccentText { get; init; }
    public Color Success { get; init; }
    public Color Warning { get; init; }
    public Color Danger { get; init; }
    public Color FocusRing { get; init; }
    public Color DisabledBackground { get; init; }
    public Color DisabledText { get; init; }
    public Color HoverOverlay { get; init; }
    public Color PressedOverlay { get; init; }
    public Color ScrollThumb { get; init; }
    public Color ScrollThumbHover { get; init; }
    public Color ScrollThumbDrag { get; init; }
    public Color Shadow { get; init; }
    public Color VisualizerBar { get; init; }

    public static ThemePalette Create(bool isLight, bool useLiquidGlass)
    {
        var isHighContrast = SystemParameters.HighContrast;
        var reduceMotion = isHighContrast || !SystemParameters.ClientAreaAnimation;
        var accent = DetectSystemAccentColor();

        return isHighContrast
            ? CreateHighContrast(isLight, useLiquidGlass, accent, reduceMotion)
            : CreateStandard(isLight, useLiquidGlass, accent, reduceMotion);
    }

    public static ThemePalette CreateDark()
    {
        var isHighContrast = SystemParameters.HighContrast;
        var reduceMotion = isHighContrast || !SystemParameters.ClientAreaAnimation;
        var accent = DetectSystemAccentColor();

        return isHighContrast
            ? CreateHighContrast(false, false, accent, reduceMotion)
            : CreateStandard(false, false, accent, reduceMotion);
    }

    public static ThemePalette CreateLight()
    {
        var isHighContrast = SystemParameters.HighContrast;
        var reduceMotion = isHighContrast || !SystemParameters.ClientAreaAnimation;
        var accent = DetectSystemAccentColor();

        return isHighContrast
            ? CreateHighContrast(true, false, accent, reduceMotion)
            : CreateStandard(true, false, accent, reduceMotion);
    }

    public static ThemePalette CreateLiquidGlass()
    {
        var accentSeed = Color.FromRgb(0x00, 0x7A, 0xFF);
        var accent = EnsureContrast(accentSeed, Color.FromRgb(0xF0, 0xF0, 0xF5), MinimumAccentContrast);

        return new ThemePalette
        {
            IsLight = true,
            IsHighContrast = false,
            UseReducedMotion = false,
            UseLiquidGlass = true,
            Background = Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF),
            Surface = Color.FromArgb(0x22, 0x00, 0x00, 0x00),
            SurfaceRaised = Color.FromArgb(0x38, 0x00, 0x00, 0x00),
            TextPrimary = Colors.Black,
            TextSecondary = Color.FromRgb(0x1C, 0x1C, 0x1E),
            TextTertiary = Color.FromRgb(0x2C, 0x2C, 0x2E),
            TextMuted = Color.FromRgb(0x3A, 0x3A, 0x3C),
            TextDim = Color.FromRgb(0x48, 0x48, 0x4A),
            TextSubtle = Color.FromRgb(0x55, 0x55, 0x58),
            TextFaint = Color.FromRgb(0x63, 0x63, 0x66),
            TextFaintest = Color.FromRgb(0x75, 0x75, 0x7A),
            Border = Color.FromArgb(0x25, 0x00, 0x00, 0x00),
            Accent = accent,
            AccentPressed = Mix(accent, Colors.Black, 0.20),
            AccentSoft = WithAlpha(accent, 0x30),
            AccentText = Colors.White,
            Success = Color.FromRgb(0x10, 0xB9, 0x81),
            Warning = Color.FromRgb(0xD9, 0x77, 0x06),
            Danger = Color.FromRgb(0xEF, 0x44, 0x44),
            FocusRing = accent,
            DisabledBackground = Color.FromArgb(0x25, 0x00, 0x00, 0x00),
            DisabledText = Color.FromRgb(0x88, 0x88, 0x88),
            HoverOverlay = Color.FromArgb(0x18, 0x00, 0x00, 0x00),
            PressedOverlay = Color.FromArgb(0x28, 0x00, 0x00, 0x00),
            ScrollThumb = Color.FromArgb(0x35, 0x00, 0x00, 0x00),
            ScrollThumbHover = Color.FromArgb(0x55, 0x00, 0x00, 0x00),
            ScrollThumbDrag = Color.FromArgb(0x75, 0x00, 0x00, 0x00),
            Shadow = Colors.Transparent,
            VisualizerBar = Colors.Black
        };
    }

    private static ThemePalette CreateStandard(bool isLight, bool useLiquidGlass, Color accentSeed, bool reduceMotion)
    {
        if (useLiquidGlass)
            return CreateLiquidGlass();

        var readabilityBackground = isLight
            ? Color.FromRgb(0xF6, 0xF8, 0xFB)
            : Color.FromRgb(0x0D, 0x0D, 0x0F);

        var background = isLight
            ? Mix(Color.FromRgb(0xF3, 0xF5, 0xF8), accentSeed, 0.05)
            : Mix(readabilityBackground, accentSeed, 0.02);

        var surface = isLight
            ? Color.FromRgb(0xFF, 0xFF, 0xFF)
            : Mix(readabilityBackground, Colors.White, 0.09);

        var surfaceRaised = isLight
            ? Mix(Color.FromRgb(0xFF, 0xFF, 0xFF), accentSeed, 0.015)
            : Mix(surface, Colors.White, 0.04);

        var textPrimary = EnsureContrast(isLight ? Color.FromRgb(0x12, 0x18, 0x22) : Color.FromRgb(0xF5, 0xF7, 0xFB), readabilityBackground, 7.0);
        var textSecondary = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.62 : 0.38), readabilityBackground, MinimumTextContrast);
        var textTertiary = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.70 : 0.48), readabilityBackground, MinimumTextContrast);
        var textMuted = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.78 : 0.58), readabilityBackground, 3.7);
        var textDim = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.82 : 0.64), readabilityBackground, 3.2);
        var textSubtle = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.86 : 0.71), readabilityBackground, 3.0);
        var textFaint = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.90 : 0.78), readabilityBackground, 2.6);
        var textFaintest = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.94 : 0.84), readabilityBackground, 2.2);
        var border = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.90 : 0.80), readabilityBackground, 1.7);

        var accent = EnsureContrast(accentSeed, readabilityBackground, MinimumAccentContrast);
        var accentPressed = Mix(accent, isLight ? Colors.Black : Colors.White, isLight ? 0.16 : 0.12);
        var accentSoft = WithAlpha(accent, (byte)(isLight ? 0x26 : 0x36));
        var accentText = PickReadableForeground(accent, MinimumTextContrast);

        var success = EnsureContrast(Color.FromRgb(0x1E, 0xB8, 0x4B), readabilityBackground, MinimumAccentContrast);
        var warning = EnsureContrast(Color.FromRgb(0xC9, 0x7A, 0x00), readabilityBackground, MinimumAccentContrast);
        var danger = EnsureContrast(Color.FromRgb(0xD8, 0x3B, 0x32), readabilityBackground, MinimumAccentContrast);
        var focusRing = EnsureContrast(accent, readabilityBackground, MinimumAccentContrast);

        var disabledBackground = Mix(surfaceRaised, textPrimary, isLight ? 0.06 : 0.12);
        var disabledText = EnsureContrast(Mix(textPrimary, readabilityBackground, isLight ? 0.88 : 0.76), readabilityBackground, 2.5);
        var hoverOverlay = WithAlpha(textPrimary, (byte)(isLight ? 0x12 : 0x1F));
        var pressedOverlay = WithAlpha(textPrimary, (byte)(isLight ? 0x20 : 0x32));
        var scrollThumb = WithAlpha(textPrimary, (byte)(isLight ? 0x45 : 0x55));
        var scrollThumbHover = WithAlpha(textPrimary, (byte)(isLight ? 0x66 : 0x72));
        var scrollThumbDrag = WithAlpha(textPrimary, (byte)(isLight ? 0x88 : 0x92));
        var shadow = WithAlpha(Colors.Black, (byte)(isLight ? 0x5A : 0x62));

        return new ThemePalette
        {
            IsLight = isLight,
            IsHighContrast = false,
            UseReducedMotion = reduceMotion,
            UseLiquidGlass = false,
            Background = background,
            Surface = surface,
            SurfaceRaised = surfaceRaised,
            TextPrimary = textPrimary,
            TextSecondary = textSecondary,
            TextTertiary = textTertiary,
            TextMuted = textMuted,
            TextDim = textDim,
            TextSubtle = textSubtle,
            TextFaint = textFaint,
            TextFaintest = textFaintest,
            Border = border,
            Accent = accent,
            AccentPressed = accentPressed,
            AccentSoft = accentSoft,
            AccentText = accentText,
            Success = success,
            Warning = warning,
            Danger = danger,
            FocusRing = focusRing,
            DisabledBackground = disabledBackground,
            DisabledText = disabledText,
            HoverOverlay = hoverOverlay,
            PressedOverlay = pressedOverlay,
            ScrollThumb = scrollThumb,
            ScrollThumbHover = scrollThumbHover,
            ScrollThumbDrag = scrollThumbDrag,
            Shadow = shadow,
            VisualizerBar = accent
        };
    }

    private static ThemePalette CreateHighContrast(bool isLight, bool useLiquidGlass, Color accentSeed, bool reduceMotion)
    {
        var background = System.Windows.SystemColors.WindowColor;
        var textPrimary = EnsureContrast(System.Windows.SystemColors.WindowTextColor, background, 7.0);
        var textSecondary = EnsureContrast(Mix(textPrimary, background, isLight ? 0.45 : 0.35), background, MinimumTextContrast);
        var border = EnsureContrast(Mix(textPrimary, background, 0.80), background, 3.0);
        var surface = Mix(background, textPrimary, isLight ? 0.03 : 0.08);
        var surfaceRaised = Mix(background, textPrimary, isLight ? 0.06 : 0.12);
        var accent = EnsureContrast(System.Windows.SystemColors.HighlightColor == background ? accentSeed : System.Windows.SystemColors.HighlightColor, background, MinimumAccentContrast);
        var accentText = EnsureContrast(System.Windows.SystemColors.HighlightTextColor, accent, MinimumTextContrast);
        var muted = EnsureContrast(System.Windows.SystemColors.GrayTextColor, background, 3.0);

        return new ThemePalette
        {
            IsLight = RelativeLuminance(background) > 0.5,
            IsHighContrast = true,
            UseReducedMotion = reduceMotion,
            UseLiquidGlass = false,
            Background = background,
            Surface = surface,
            SurfaceRaised = surfaceRaised,
            TextPrimary = textPrimary,
            TextSecondary = textSecondary,
            TextTertiary = textSecondary,
            TextMuted = muted,
            TextDim = muted,
            TextSubtle = muted,
            TextFaint = muted,
            TextFaintest = muted,
            Border = border,
            Accent = accent,
            AccentPressed = Mix(accent, textPrimary, 0.16),
            AccentSoft = WithAlpha(accent, 0x30),
            AccentText = accentText,
            Success = EnsureContrast(Color.FromRgb(0x00, 0x88, 0x00), background, MinimumAccentContrast),
            Warning = EnsureContrast(Color.FromRgb(0x9A, 0x57, 0x00), background, MinimumAccentContrast),
            Danger = EnsureContrast(Color.FromRgb(0xAA, 0x00, 0x00), background, MinimumAccentContrast),
            FocusRing = accent,
            DisabledBackground = Mix(background, textPrimary, 0.08),
            DisabledText = muted,
            HoverOverlay = WithAlpha(textPrimary, 0x18),
            PressedOverlay = WithAlpha(textPrimary, 0x2A),
            ScrollThumb = WithAlpha(textPrimary, 0x55),
            ScrollThumbHover = WithAlpha(textPrimary, 0x72),
            ScrollThumbDrag = WithAlpha(textPrimary, 0x88),
            Shadow = WithAlpha(Colors.Black, 0x18),
            VisualizerBar = accent
        };
    }

    public static Color DetectSystemAccentColor()
    {
        try
        {
            var settings = new UISettings();
            var color = settings.GetColorValue(UIColorType.Accent);
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
        catch
        {
            return SystemParameters.WindowGlassColor;
        }
    }

    private static Color PickReadableForeground(Color background, double minContrast)
    {
        var whiteContrast = ContrastRatio(Colors.White, background);
        var blackContrast = ContrastRatio(Colors.Black, background);
        if (whiteContrast >= minContrast || whiteContrast >= blackContrast)
            return Colors.White;

        return Colors.Black;
    }

    private static Color EnsureContrast(Color foreground, Color background, double minContrast)
    {
        if (ContrastRatio(foreground, background) >= minContrast)
            return foreground;

        var lighter = Colors.White;
        var darker = Colors.Black;
        var lighten = ContrastRatio(lighter, background) >= ContrastRatio(darker, background);
        var candidate = foreground;

        for (int i = 0; i < 24; i++)
        {
            candidate = Mix(candidate, lighten ? lighter : darker, 0.12);
            if (ContrastRatio(candidate, background) >= minContrast)
                return candidate;
        }

        return lighten ? lighter : darker;
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLum = RelativeLuminance(first);
        var secondLum = RelativeLuminance(second);
        var lighter = Math.Max(firstLum, secondLum);
        var darker = Math.Min(firstLum, secondLum);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        var r = ToLinear(color.R / 255.0);
        var g = ToLinear(color.G / 255.0);
        var b = ToLinear(color.B / 255.0);
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double ToLinear(double channel)
    {
        return channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static Color Mix(Color baseColor, Color mixColor, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(baseColor.A + ((mixColor.A - baseColor.A) * amount)),
            (byte)Math.Round(baseColor.R + ((mixColor.R - baseColor.R) * amount)),
            (byte)Math.Round(baseColor.G + ((mixColor.G - baseColor.G) * amount)),
            (byte)Math.Round(baseColor.B + ((mixColor.B - baseColor.B) * amount)));
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
