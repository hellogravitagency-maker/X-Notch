namespace XNotch.Island;

/// <summary>
/// Every island size, radius, offset and timing in device-independent pixels / ms.
/// No magic numbers elsewhere: views and the window read from here.
/// Concentric rule: an inner element's radius = outer radius − its inset.
/// </summary>
public static class IslandMetrics
{
    // Placement
    /// <summary>Floating style: gap between screen top and island top.</summary>
    public const double FloatingTopOffset = 8;
    /// <summary>Extra window space below the island so glow and shadow never clip.</summary>
    public const double WindowGlowMargin = GlowBlur + ShadowOffsetY + 8;

    // Idle
    public const double IdleWidth = 120;
    public const double IdleHeight = 32;

    // Compact
    public const double CompactMinWidth = 240;
    public const double CompactMaxWidth = 340;
    public const double CompactHeight = 32;
    public const double LeadingSlot = 22;
    public const double LeadingInset = 6;
    public const double TrailingInset = 10;
    /// <summary>Space reserved between leading and trailing content (camera / centre).</summary>
    public const double CompactCenterGap = 28;
    public const double CompactArtRadius = 6;

    // Minimal
    public const double MinimalPillWidth = 200;
    public const double MinimalCircle = 32;
    public const double MinimalGap = 8;

    // Alert / Hud
    public const double AlertWidth = 340;
    public const double AlertHeight = 64;
    public const double AlertRadius = 32;
    public const double AlertIcon = 32;
    public const double AlertPadding = 16;
    public const double HudSliderHeight = 4;

    // Expanded
    public const double ExpandedRadius = 36;
    public const double ExpandedPadding = 16;
    /// <summary>36 − 16 = 20: inner cards and album art.</summary>
    public const double ExpandedInnerRadius = ExpandedRadius - ExpandedPadding;
    public const double ExpandedArt = 64;
    public const double ScrubberHeight = 4;
    public const double TransportButton = 28;

    // Spotlight
    public const double SpotlightWidth = 600;
    public const double SpotlightHeight = 56;
    public const double SpotlightResultsHeight = 360;
    public const double SpotlightRadius = 28;

    // Peek (hover on Idle / Compact / Minimal)
    public const double PeekGrowWidth = 6;
    public const double PeekGrowHeight = 2;

    // Shadow (Expanded only) and album glow
    public const double ShadowBlur = 40;
    public const double ShadowOffsetY = 10;
    public const double ShadowOpacity = 0.45;
    public const double GlowBlur = 40;
    public const double GlowOpacity = 0.25;

    // Waveform (music compact)
    public const int WaveBars = 4;
    public const double WaveBarWidth = 3;
    public const double WaveBarRadius = 1.5;
    public const double WaveBarGap = 2;
    public const double WaveBarMin = 4;
    public const double WaveBarMax = 16;

    // Rings
    public const double TimerRing = 18;
    public const double RingStroke = 2;

    // Assistant glow
    public const double AssistantRing = 2;
    public const double AssistantBlur = 12;
    public const double AssistantPeriodSeconds = 3;
    public const int AssistantBitmapSize = 256;

    // Goo (minimal split / merge)
    public const double GooRadius = 16;
    /// <summary>Neck breaks when centre distance exceeds r1 + r2 + this.</summary>
    public const double GooBreakSlack = 10;
    public const double GooSpread = 0.5;
    public const double GooHandle = 2.4;

    // Choreography (ms)
    public const int ContentEnterDelayMs = 90;
    public const int ContentExitMs = 120;
    public const double ContentEnterScale = 0.92;
    public const double ContentEnterBlur = 8;
    public const double ContentExitBlur = 6;
    public const int SquashMs = 80;
    public const double SquashScaleX = 1.04;
    public const double SquashScaleY = 0.94;
    public const int ReducedFadeMs = 150;
    public const int LongPressMs = 500;
    public const int AlertTickMs = 100;

    // Typography
    public const string FontFamily = "SF Pro Display, Segoe UI Variable Display, Segoe UI";
    public const double CompactFontSize = 13;
    public const double TitleFontSize = 15;
    public const double SubtitleFontSize = 13;
    public const double NumberFontSize = 15;
    public const double SecondaryOpacity = 0.55;

    /// <summary>Capsule radius for a height.</summary>
    public static double Capsule(double height) => height / 2;

    /// <summary>Concentric inner radius.</summary>
    public static double Concentric(double outerRadius, double inset) => System.Math.Max(0, outerRadius - inset);
}

/// <summary>Accent colours. The island itself is never coloured; colour comes from content.</summary>
public static class IslandColors
{
    public const string Fill = "#000000";
    public const string TextPrimary = "#FFFFFF";
    public const string Timer = "#FF9F0A";
    public const string Success = "#34C759";
    public const string Destructive = "#FF3B30";
    public const string Info = "#0A84FF";
    public const string LowBattery = "#FF453A";
    public const string Focus = "#5E5CE6";
    public const string ScrubberTrack = "#33FFFFFF"; // white 20 %
    /// <summary>Assistant conic gradient stops (closed loop).</summary>
    public static readonly string[] AssistantStops = ["#FF2D55", "#AF52DE", "#0A84FF", "#5AC8FA", "#FF2D55"];
}
