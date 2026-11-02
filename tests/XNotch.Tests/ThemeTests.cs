using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using XNotch.Views;
using Xunit;

namespace XNotch.Tests;

[Collection("WpfSequential")]
public class ThemeTests
{
    private static readonly object AppLock = new();

    private static Application EnsureApplication()
    {
        lock (AppLock)
        {
            if (Application.Current != null) return Application.Current;
            try { return new Application(); } catch (InvalidOperationException) { return Application.Current!; }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = EnsureApplication();
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
        {
            throw new AggregateException(caught);
        }
    }

    [Fact]
    public void ColorHelper_ParseColor_VariousFormats_ParsesAccurately()
    {
        var blue = ColorHelper.ParseColor("#007AFF", Colors.Black);
        Assert.Equal(0x00, blue.R);
        Assert.Equal(0x7A, blue.G);
        Assert.Equal(0xFF, blue.B);

        var green = ColorHelper.ParseColor("10B981", Colors.Black);
        Assert.Equal(0x10, green.R);
        Assert.Equal(0xB9, green.G);
        Assert.Equal(0x81, green.B);

        var fallback = ColorHelper.ParseColor("invalid-color-value", Colors.Red);
        Assert.Equal(Colors.Red, fallback);
    }

    [Fact]
    public void LiquidGlass_ProducesPureBlackText_AndHighContrast()
    {
        var palette = ThemePalette.CreateLiquidGlass();

        Assert.True(palette.UseLiquidGlass);
        Assert.True(palette.IsLight);
        Assert.Equal(Colors.Black, palette.TextPrimary);
        Assert.True(palette.TextSecondary.R <= 0x25);
        Assert.Equal(Colors.Black, palette.VisualizerBar);
        Assert.Equal(0x75, palette.Background.A);
    }

    [Fact]
    public void DarkTheme_ProducesStandardDarkBackground_AndReadableText()
    {
        var palette = ThemePalette.CreateDark();

        Assert.False(palette.IsLight);
        Assert.False(palette.UseLiquidGlass);
        Assert.True(palette.Background.R <= 0x20 && palette.Background.G <= 0x20 && palette.Background.B <= 0x20);
        Assert.True(palette.TextPrimary.R >= 0xE0);
    }

    [Fact]
    public void LightTheme_ProducesLightBackground_AndDarkText()
    {
        var palette = ThemePalette.CreateLight();

        Assert.True(palette.IsLight);
        Assert.False(palette.UseLiquidGlass);
        Assert.True(palette.Background.R >= 0xDC && palette.Background.G >= 0xDC && palette.Background.B >= 0xDC);
        Assert.True(palette.TextPrimary.R <= 0x30);
    }

    [Fact]
    public void AdaptiveTheme_GeneratesValidPalette_WithSystemAccent()
    {
        var palette = ThemePalette.Create(isLight: false, useLiquidGlass: false);
        Assert.False(palette.IsLight);
        Assert.NotEqual(Colors.Transparent, palette.Accent);

        var lightPalette = ThemePalette.Create(isLight: true, useLiquidGlass: false);
        Assert.True(lightPalette.IsLight);
        Assert.NotEqual(Colors.Transparent, lightPalette.Accent);
    }

    [Fact]
    public void ThemeService_ApplyThemes_AndLiquidGlass_SwapsAndAppliesResources()
    {
        RunOnSta(() =>
        {
            var themeService = new ThemeService();
            var settings = new AppSettings
            {
                Theme = AppTheme.Light,
                AdaptToWindowsTheme = false,
                UseLiquidGlassTheme = false
            };

            themeService.Apply(settings);

            var textBrush = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
            var fillBrush = (SolidColorBrush)Application.Current.Resources["NotchFillBrush"];
            Assert.True(textBrush.Color.R <= 0x30);
            Assert.True(fillBrush.Color.R >= 0xDC);

            // Switch to Liquid Glass via AppTheme.LiquidGlass
            settings.Theme = AppTheme.LiquidGlass;
            settings.UseLiquidGlassTheme = false;
            themeService.Apply(settings);

            var glassTextBrush = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
            var glassFillBrush = (SolidColorBrush)Application.Current.Resources["NotchFillBrush"];
            Assert.Equal(Colors.Black, glassTextBrush.Color);
            Assert.Equal(0x75, glassFillBrush.Color.A);
            Assert.Equal(Colors.Black, (Color)Application.Current.Resources["VisualizerBarColor"]);

            // Switch to Dark Theme
            settings.Theme = AppTheme.Dark;
            settings.UseLiquidGlassTheme = false;
            themeService.Apply(settings);

            var darkFillBrush = (SolidColorBrush)Application.Current.Resources["NotchFillBrush"];
            var darkTextBrush = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
            Assert.True(darkFillBrush.Color.R <= 0x20);
            Assert.True(darkTextBrush.Color.R >= 0xDC);
        });
    }

    [Fact]
    public void InlineSettingsView_LoadAndChangeTheme_HandlesLightDarkAndGlass()
    {
        RunOnSta(() =>
        {
            var settingsView = new InlineSettingsView();
            var settings = new AppSettings
            {
                Theme = AppTheme.Light,
                AdaptToWindowsTheme = false
            };

            settingsView.LoadSettings(settings);
            Assert.True(settingsView.ThemeLightRadio.IsChecked);
            Assert.False(settingsView.AdaptToWindowsThemeCheck.IsChecked);

            // Change to Liquid Glass
            settings.Theme = AppTheme.LiquidGlass;
            settingsView.LoadSettings(settings);
            Assert.True(settingsView.ThemeGlassRadio.IsChecked);

            // Change to Dark Theme
            settings.Theme = AppTheme.Dark;
            settingsView.LoadSettings(settings);
            Assert.True(settingsView.ThemeDarkRadio.IsChecked);

            // Change to Adaptive
            settings.AdaptToWindowsTheme = true;
            settingsView.LoadSettings(settings);
            Assert.True(settingsView.AdaptToWindowsThemeCheck.IsChecked);
        });
    }

    [Fact]
    public void ThemeService_PluginThemeOverride_WorksCorrectly()
    {
        RunOnSta(() =>
        {
            var service = new ThemeService();
            var custom = ThemePalette.CreateCustom(
                System.Windows.Media.Color.FromRgb(10, 10, 10),
                System.Windows.Media.Color.FromRgb(20, 20, 20),
                System.Windows.Media.Color.FromRgb(30, 30, 30),
                System.Windows.Media.Colors.White,
                System.Windows.Media.Color.FromRgb(40, 40, 40),
                System.Windows.Media.Colors.Cyan
            );

            service.SetPluginThemeOverride(custom);
            Assert.Equal(custom, service.PluginThemeOverride);
            Assert.Equal(System.Windows.Media.Colors.Cyan, service.CurrentPalette.Accent);

            service.SetPluginThemeOverride(null);
            Assert.Null(service.PluginThemeOverride);
        });
    }
}
