using System.Threading.Tasks;
using System.Windows.Media;
using XNotch.Plugins;
using XNotch.Services;

namespace NordThemePlugin;

[XNotchPlugin("com.xnotch.nordtheme", "Nord Arctic Theme", "1.0.0", "X-Notch Team")]
public class NordThemePlugin : PluginBase, IThemePlugin
{
    public override string Id => "com.xnotch.nordtheme";
    public override string Name => "Nord Arctic Theme";
    public override string Version => "1.0.0";
    public override string Author => "X-Notch Team";
    public override string Description => "An arctic, north-bluish clean dark palette based on the legendary Nord design system.";
    public override string MinimumXNotchVersion => "0.7.0";

    public ThemePalette GetThemePalette()
    {
        return ThemePalette.CreateCustom(
            background: Color.FromRgb(0x2E, 0x34, 0x40),      // Nord0
            surface: Color.FromRgb(0x3B, 0x42, 0x52),         // Nord1
            surfaceRaised: Color.FromRgb(0x43, 0x4C, 0x5E),   // Nord2
            textPrimary: Color.FromRgb(0xEC, 0xEF, 0xF4),     // Nord6
            border: Color.FromRgb(0x4C, 0x56, 0x6A),          // Nord3
            accent: Color.FromRgb(0x88, 0xC0, 0xD0),          // Nord8 Frost
            isLight: false,
            visualizerBar: Color.FromRgb(0x81, 0xA1, 0xC1),   // Nord9 Frost
            success: Color.FromRgb(0xA3, 0xBE, 0x8C),         // Nord14 Aurora Green
            warning: Color.FromRgb(0xEB, 0xCB, 0x8B),         // Nord13 Aurora Yellow
            danger: Color.FromRgb(0xBF, 0x61, 0x6A)           // Nord11 Aurora Red
        );
    }
}
