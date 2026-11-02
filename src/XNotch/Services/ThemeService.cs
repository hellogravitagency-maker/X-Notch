using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using XNotch.Helpers;
using XNotch.Models;

namespace XNotch.Services;

public class ThemeService
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";
    private const double FadeDurationMs = 150;

    private AppTheme _currentSetting = AppTheme.Dark;
    private bool _isLightResolved;
    private bool _isTransitioning;
    private bool _useLiquidGlass;
    private bool _adaptToWindowsTheme;

    public event Action? ThemeChanged;

    public bool IsLight => _isLightResolved;
    public bool IsLiquidGlass => _useLiquidGlass;
    public ThemePalette CurrentPalette { get; private set; } = ThemePalette.Create(false, false);

    public void Apply(AppSettings settings)
    {
        bool isGlass = settings.Theme == AppTheme.LiquidGlass || settings.UseLiquidGlassTheme;
        ApplyInternal(
            settings.GetRequestedTheme(),
            settings.AdaptToWindowsTheme,
            isGlass);
    }

    public void Apply(AppTheme theme, bool useLiquidGlass = false)
    {
        ApplyInternal(
            theme,
            theme == AppTheme.Auto,
            useLiquidGlass);
    }

    public async Task ApplyWithTransition(AppSettings settings, UIElement target)
    {
        await ApplyWithTransition(
            settings.GetRequestedTheme(),
            settings.AdaptToWindowsTheme,
            target,
            settings.UseLiquidGlassTheme);
    }

    public async Task ApplyWithTransition(AppTheme theme, UIElement target, bool useLiquidGlass = false)
    {
        await ApplyWithTransition(
            theme,
            theme == AppTheme.Auto,
            target,
            useLiquidGlass);
    }

    public void StartWatchingSystemTheme()
    {
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    public void StopWatchingSystemTheme()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
    }

    private async Task ApplyWithTransition(
        AppTheme theme,
        bool adaptToWindowsTheme,
        UIElement target,
        bool useLiquidGlass)
    {
        if (_isTransitioning) return;

        var previewPalette = BuildPalette(theme, useLiquidGlass);
        if (previewPalette.UseReducedMotion)
        {
            ApplyInternal(theme, adaptToWindowsTheme, useLiquidGlass, previewPalette);
            return;
        }

        _isTransitioning = true;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(FadeDurationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var fadeOutCompletion = new TaskCompletionSource();
        fadeOut.Completed += (_, _) => fadeOutCompletion.TrySetResult();
        target.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await fadeOutCompletion.Task;

        ApplyInternal(theme, adaptToWindowsTheme, useLiquidGlass, previewPalette);

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(FadeDurationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeInCompletion = new TaskCompletionSource();
        fadeIn.Completed += (_, _) => fadeInCompletion.TrySetResult();
        target.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        await fadeInCompletion.Task;

        _isTransitioning = false;
    }

    private void ApplyInternal(
        AppTheme theme,
        bool adaptToWindowsTheme,
        bool useLiquidGlass,
        ThemePalette? paletteOverride = null)
    {
        if (theme == AppTheme.LiquidGlass)
        {
            useLiquidGlass = true;
        }
        _currentSetting = theme;
        _adaptToWindowsTheme = adaptToWindowsTheme;
        _useLiquidGlass = useLiquidGlass;

        CurrentPalette = paletteOverride ?? BuildPalette(theme, useLiquidGlass);
        _isLightResolved = CurrentPalette.IsLight;

        SwapThemeDictionary();
        ApplyPaletteResources(CurrentPalette);
        ThemeChanged?.Invoke();
    }

    private ThemePalette BuildPalette(
        AppTheme theme,
        bool useLiquidGlass)
    {
        if (useLiquidGlass || theme == AppTheme.LiquidGlass)
            return ThemePalette.CreateLiquidGlass();

        return theme switch
        {
            AppTheme.Light => ThemePalette.CreateLight(),
            AppTheme.Dark => ThemePalette.CreateDark(),
            _ => DetectWindowsThemeIsLight()
                ? ThemePalette.CreateLight()
                : ThemePalette.CreateDark()
        };
    }

    private void SwapThemeDictionary()
    {
        string themeFile;
        if (_useLiquidGlass || _currentSetting == AppTheme.LiquidGlass)
        {
            themeFile = "LiquidGlassTheme.xaml";
        }
        else if (_isLightResolved)
        {
            themeFile = "LightTheme.xaml";
        }
        else
        {
            themeFile = "DarkTheme.xaml";
        }

        string asmName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "X-Notch";
        var dictUri = new Uri($"pack://application:,,,/{asmName};component/Views/Components/{themeFile}", UriKind.Absolute);

        var app = Application.Current;
        if (app != null)
        {
            var mergedDicts = app.Resources.MergedDictionaries;

            for (int i = mergedDicts.Count - 1; i >= 0; i--)
            {
                if (mergedDicts[i].Source != null &&
                    (mergedDicts[i].Source.OriginalString.Contains("DarkTheme") ||
                     mergedDicts[i].Source.OriginalString.Contains("LightTheme") ||
                     mergedDicts[i].Source.OriginalString.Contains("LiquidGlassTheme")))
                {
                    mergedDicts.RemoveAt(i);
                }
            }

            mergedDicts.Insert(0, new ResourceDictionary { Source = dictUri });
        }
    }

    private void ApplyPaletteResources(ThemePalette palette)
    {
        if (Application.Current == null) return;

        UpdateBrush("BackgroundBrush", palette.Background);
        UpdateBrush("SurfaceBrush", palette.Surface);
        UpdateBrush("SurfaceRaisedBrush", palette.SurfaceRaised);
        UpdateBrush("TextPrimaryBrush", palette.TextPrimary);
        UpdateBrush("TextSecondaryBrush", palette.TextSecondary);
        UpdateBrush("TextTertiaryBrush", palette.TextTertiary);
        UpdateBrush("TextMutedBrush", palette.TextMuted);
        UpdateBrush("TextDimBrush", palette.TextDim);
        UpdateBrush("TextSubtleBrush", palette.TextSubtle);
        UpdateBrush("TextFaintBrush", palette.TextFaint);
        UpdateBrush("TextFaintestBrush", palette.TextFaintest);
        UpdateBrush("BorderBrush", palette.Border);
        UpdateBrush("AccentBrush", palette.Accent);
        UpdateBrush("AccentPressedBrush", palette.AccentPressed);
        UpdateBrush("AccentSoftBrush", palette.AccentSoft);
        UpdateBrush("AccentTextBrush", palette.AccentText);
        UpdateBrush("SuccessBrush", palette.Success);
        UpdateBrush("WarningBrush", palette.Warning);
        UpdateBrush("DangerBrush", palette.Danger);
        UpdateBrush("FocusRingBrush", palette.FocusRing);
        UpdateBrush("DisabledBackgroundBrush", palette.DisabledBackground);
        UpdateBrush("DisabledTextBrush", palette.DisabledText);

        UpdateBrush("NotchFillBrush", palette.Background);
        UpdateBrush("NotchShadowBrush", Colors.Transparent);
        UpdateBrush("NotchBorderBrush", Colors.Transparent);
        UpdateBrush("SettingLabelBrush", palette.TextSecondary);
        UpdateBrush("BatteryTextBrush", palette.TextPrimary);
        UpdateBrush("SeparatorBrush", palette.Border);
        UpdateBrush("ProgressTrackBrush", MixForTrack(palette.SurfaceRaised, palette.Border, palette.IsLight));
        UpdateBrush("ProgressFillBrush", palette.Accent);
        UpdateBrush("SegmentedBgBrush", palette.Surface);
        UpdateBrush("SegmentedCheckedBgBrush", palette.SurfaceRaised);
        UpdateBrush("ToggleTrackOffBrush", palette.DisabledBackground);
        UpdateBrush("ToggleThumbBrush", palette.IsLight ? Colors.White : palette.SurfaceRaised);
        UpdateBrush("SegmentedTextBrush", palette.TextSecondary);
        UpdateBrush("SegmentedHoverBgBrush", palette.HoverOverlay);
        UpdateBrush("SegmentedHoverTextBrush", palette.TextPrimary);
        UpdateBrush("SegmentedCheckedTextBrush", palette.TextPrimary);
        UpdateBrush("TransportHoverBrush", palette.HoverOverlay);
        UpdateBrush("TransportPressedBrush", palette.PressedOverlay);
        UpdateBrush("HoverOverlayBrush", palette.HoverOverlay);
        UpdateBrush("PressedOverlayBrush", palette.PressedOverlay);
        UpdateBrush("ScrollThumbBrush", palette.ScrollThumb);
        UpdateBrush("ScrollThumbHoverBrush", palette.ScrollThumbHover);
        UpdateBrush("ScrollThumbDragBrush", palette.ScrollThumbDrag);
        UpdateBrush("BatteryBorderBrush", palette.Border);
        UpdateBrush("BatteryNormalFillBrush", palette.TextPrimary);
        UpdateBrush("HudIconBrush", palette.TextPrimary);
        UpdateBrush("HudSliderFillBrush", palette.Accent);
        UpdateBrush("PluginAccentBrush", palette.Accent);
        UpdateBrush("PluginAccentPressedBrush", palette.AccentPressed);
        UpdateBrush("PluginAccentSoftBrush", palette.AccentSoft);
        UpdateBrush("PluginPositiveBrush", palette.Success);
        UpdateBrush("PluginNegativeBrush", palette.Danger);
        UpdateBrush("DropZoneBrush", WithAlpha(palette.Accent, palette.IsLight ? (byte)0x16 : (byte)0x22));
        UpdateBrush("DropZoneGlowBrush", WithAlpha(palette.Accent, palette.IsLight ? (byte)0x18 : (byte)0x28));

        // When Liquid Glass is active, ensure crisp solid black text and widgets
        if (_useLiquidGlass)
        {
            UpdateBrush("TextPrimaryBrush", Colors.Black);
            UpdateBrush("TextSecondaryBrush", Color.FromRgb(0x1C, 0x1C, 0x1E));
            UpdateBrush("TextTertiaryBrush", Color.FromRgb(0x2C, 0x2C, 0x2E));
            UpdateBrush("TextMutedBrush", Color.FromRgb(0x3A, 0x3A, 0x3C));
            UpdateBrush("TextDimBrush", Color.FromRgb(0x48, 0x48, 0x4A));
            UpdateBrush("TextSubtleBrush", Color.FromRgb(0x55, 0x55, 0x58));
            UpdateBrush("TextFaintBrush", Color.FromRgb(0x63, 0x63, 0x66));
            UpdateBrush("TextFaintestBrush", Color.FromRgb(0x75, 0x75, 0x7A));
            UpdateBrush("SettingLabelBrush", Color.FromRgb(0x11, 0x11, 0x11));
            UpdateBrush("BatteryTextBrush", Colors.Black);
            UpdateBrush("BatteryNormalFillBrush", Colors.Black);
            UpdateBrush("HudIconBrush", Colors.Black);
            UpdateBrush("HudSliderFillBrush", Colors.Black);
            UpdateBrush("ProgressFillBrush", Colors.Black);
            UpdateBrush("SegmentedCheckedTextBrush", Colors.Black);
            UpdateBrush("NotchFillBrush", Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF));
            UpdateBrush("NotchBorderBrush", Colors.Transparent);
            UpdateBrush("NotchShadowBrush", Colors.Transparent);
            UpdateBrush("BackgroundBrush", Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF));
            UpdateBrush("SurfaceBrush", Color.FromArgb(0x22, 0x00, 0x00, 0x00));
            UpdateBrush("SurfaceRaisedBrush", Color.FromArgb(0x38, 0x00, 0x00, 0x00));
            UpdateBrush("BorderBrush", Color.FromArgb(0x25, 0x00, 0x00, 0x00));
            UpdateBrush("SeparatorBrush", Color.FromArgb(0x25, 0x00, 0x00, 0x00));
            Application.Current.Resources["VisualizerBarColor"] = Colors.Black;
        }
        else
        {
            Application.Current.Resources["VisualizerBarColor"] = palette.VisualizerBar;
        }
    }

    private void UpdateBrush(string key, Color color)
    {
        if (Application.Current == null) return;

        var brush = FindBrushResource(key);
        if (brush != null && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static SolidColorBrush? FindBrushResource(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
            return brush;

        return null;
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!_adaptToWindowsTheme && e.Category != UserPreferenceCategory.Accessibility)
            return;

        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Accessibility
            or UserPreferenceCategory.Window)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                ApplyInternal(
                    _adaptToWindowsTheme ? AppTheme.Auto : _currentSetting,
                    _adaptToWindowsTheme,
                    _useLiquidGlass)));
        }
    }

    private static bool DetectWindowsThemeIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var value = key?.GetValue(RegistryValueName);
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private static Color MixForTrack(Color surface, Color border, bool isLight)
    {
        return isLight
            ? Mix(surface, border, 0.55)
            : Mix(surface, border, 0.42);
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
