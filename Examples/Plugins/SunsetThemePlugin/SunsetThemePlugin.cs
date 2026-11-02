using System.Threading.Tasks;
using System.Windows.Media;
using XNotch.Plugins;
using XNotch.Services;

namespace SunsetThemePlugin;

[XNotchPlugin("com.xnotch.sunsettheme", "Sunset Glow Theme", "1.0.0", "X-Notch Team")]
public class SunsetThemePlugin : PluginBase, IThemePlugin
{
    public override string Id => "com.xnotch.sunsettheme";
    public override string Name => "Sunset Glow Theme";
    public override string Version => "1.0.0";
    public override string Author => "X-Notch Team";
    public override string Description => "Warm twilight ambiance featuring deep berry dusk tones and glowing sunset orange & coral accents.";
    public override string MinimumXNotchVersion => "0.7.0";

    public ThemePalette GetThemePalette()
    {
        return ThemePalette.CreateCustom(
            background: Color.FromRgb(0x18, 0x11, 0x24),
            surface: Color.FromRgb(0x27, 0x1C, 0x38),
            surfaceRaised: Color.FromRgb(0x38, 0x27, 0x50),
            textPrimary: Color.FromRgb(0xFF, 0xF4, 0xE6),
            border: Color.FromRgb(0x4C, 0x34, 0x66),
            accent: Color.FromRgb(0xFF, 0x7E, 0x47), // Sunset Orange
            isLight: false,
            visualizerBar: Color.FromRgb(0xFF, 0x53, 0x76), // Coral Pink
            success: Color.FromRgb(0x34, 0xD3, 0x99),
            warning: Color.FromRgb(0xFB, 0xBF, 0x24),
            danger: Color.FromRgb(0xF4, 0x3F, 0x5E)
        );
    }
}
