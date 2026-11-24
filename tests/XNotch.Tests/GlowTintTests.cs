using System.Windows.Media;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class GlowTintTests
{
    private static (double S, double L) Hsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = System.Math.Max(r, System.Math.Max(g, b)), min = System.Math.Min(r, System.Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        return (d == 0 ? 0 : d / (1 - System.Math.Abs(2 * l - 1)), l);
    }

    [Theory]
    [InlineData(0x20, 0x05, 0x10)]   // very dark wine
    [InlineData(0xF0, 0xE8, 0xF8)]   // washed-out lilac
    [InlineData(0x10, 0xC0, 0x90)]   // saturated teal
    public void ToGlowTint_ClampsIntoVisibleRange(byte r, byte g, byte b)
    {
        var (s, l) = Hsl(ColorExtractionService.ToGlowTint(Color.FromRgb(r, g, b)));
        Assert.InRange(l, 0.54, 0.69);
        Assert.True(s >= 0.58, $"saturation {s}");
    }

    [Fact]
    public void ToGlowTint_GreyFallsBackToSoftWhite()
    {
        Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xF0), ColorExtractionService.ToGlowTint(Colors.DimGray));
    }

    [Fact]
    public void ToGlowTint_KeepsHueFamily()
    {
        var t = ColorExtractionService.ToGlowTint(Color.FromRgb(0x30, 0x00, 0x00)); // dark red
        Assert.True(t.R > t.G && t.R > t.B);
    }
}
