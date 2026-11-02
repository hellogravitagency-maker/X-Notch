using XNotch.Services;

namespace XNotch.Plugins;

/// <summary>
/// Plugin that supplies a custom dynamic theme palette for X-Notch.
/// When enabled, the theme palette is automatically applied to all island surfaces and widgets.
/// </summary>
public interface IThemePlugin : IPlugin
{
    /// <summary>
    /// Gets the custom theme palette provided by this plugin.
    /// </summary>
    ThemePalette GetThemePalette();
}
