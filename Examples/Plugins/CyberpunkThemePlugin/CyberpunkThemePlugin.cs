using System.Threading.Tasks;
using System.Windows.Media;
using XNotch.Plugins;
using XNotch.Services;

namespace CyberpunkThemePlugin;

[XNotchPlugin("com.xnotch.cyberpunktheme", "Cyberpunk Neon Theme", "1.0.0", "X-Notch Team")]
public class CyberpunkThemePlugin : PluginBase, IThemePlugin
{
    public override string Id => "com.xnotch.cyberpunktheme";
    public override string Name => "Cyberpunk Neon Theme";
    public override string Version => "1.0.0";
    public override string Author => "X-Notch Team";
    public override string Description => "High-tech cyberpunk aesthetic featuring deep obsidian tones and vivid cyan & neon pink accents.";
    public override string MinimumXNotchVersion => "0.7.0";

    public ThemePalette GetThemePalette()
    {
        return ThemePalette.CreateCustom(
            background: Color.FromRgb(0x0B, 0x0B, 0x14),
            surface: Color.FromRgb(0x16, 0x16, 0x26),
            surfaceRaised: Color.FromRgb(0x23, 0x23, 0x38),
            textPrimary: Color.FromRgb(0xF0, 0xF6, 0xFC),
            border: Color.FromRgb(0x3B, 0x2D, 0x54),
            accent: Color.FromRgb(0x00, 0xF0, 0xFF),
            isLight: false,
            visualizerBar: Color.FromRgb(0xFF, 0x00, 0x7F),
            success: Color.FromRgb(0x00, 0xFF, 0x9D),
            warning: Color.FromRgb(0xFF, 0xE6, 0x00),
            danger: Color.FromRgb(0xFF, 0x00, 0x55)
        );
    }
}
