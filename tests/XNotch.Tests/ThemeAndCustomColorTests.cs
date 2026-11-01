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
public class ThemeAndCustomColorTests
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
    public void WhiteTheme_ProducesPureWhiteBackground_AndBlackText()
    {
        var palette = ThemePalette.CreateWhite();

        Assert.True(palette.IsLight);
        Assert.False(palette.UseLiquidGlass);
        Assert.Equal(Colors.White, palette.Background);
        Assert.Equal(Colors.Black, palette.TextPrimary);
        Assert.Equal(Colors.White, palette.SurfaceRaised);
        Assert.Equal(Colors.Black, palette.VisualizerBar);
    }

    [Fact]
    public void CustomTheme_AutoContrastEngine_AdaptsTextAccordingToBackgroundLuminance()
    {
        // Dark Navy background should yield light/white text
        var darkNavy = Color.FromRgb(0x0F, 0x17, 0x2A);
        var darkPalette = ThemePalette.CreateCustom(darkNavy, Color.FromRgb(0x00, 0x7A, 0xFF));
        Assert.False(darkPalette.IsLight);
        Assert.True(darkPalette.TextPrimary.R >= 0xE0);

        // Pure Snow White background should yield pure black text
        var pureWhite = Colors.White;
        var lightPalette = ThemePalette.CreateCustom(pureWhite, Color.FromRgb(0x00, 0x7A, 0xFF));
        Assert.True(lightPalette.IsLight);
        Assert.Equal(Colors.Black, lightPalette.TextPrimary);
    }

    [Fact]
    public void ThemeService_ApplyWhiteTheme_AndLiquidGlass_SwapsAndAppliesResources()
    {
        RunOnSta(() =>
        {
            var themeService = new ThemeService();
            var settings = new AppSettings
            {
                Theme = AppTheme.White,
                AdaptToWindowsTheme = false,
                UseLiquidGlassTheme = false,
                CustomAccentColor = "#007AFF"
            };

            themeService.Apply(settings);

            var textBrush = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
            var fillBrush = (SolidColorBrush)Application.Current.Resources["NotchFillBrush"];
            Assert.Equal(Colors.Black, textBrush.Color);
            Assert.Equal(Colors.White, fillBrush.Color);

            // Now switch to Liquid Glass via AppTheme.LiquidGlass
            settings.Theme = AppTheme.LiquidGlass;
            settings.UseLiquidGlassTheme = false; // Even if false, AppTheme.LiquidGlass activates glass
            themeService.Apply(settings);

            var glassTextBrush = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
            var glassFillBrush = (SolidColorBrush)Application.Current.Resources["NotchFillBrush"];
            Assert.Equal(Colors.Black, glassTextBrush.Color);
            Assert.Equal(0x75, glassFillBrush.Color.A);
            Assert.Equal(Colors.Black, (Color)Application.Current.Resources["VisualizerBarColor"]);

            // Now test Custom Theme
            settings.Theme = AppTheme.Custom;
            settings.UseLiquidGlassTheme = false;
            settings.CustomBackgroundColor = "#180D2B";
            settings.CustomAccentColor = "#06B6D4";
            themeService.Apply(settings);

            var customFillBrush = (SolidColorBrush)Application.Current.Resources["NotchFillBrush"];
            var customAccentBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            Assert.Equal(0x18, customFillBrush.Color.R);
            Assert.Equal(0x0D, customFillBrush.Color.G);
            Assert.Equal(0x2B, customFillBrush.Color.B);
            Assert.Equal(0x06, customAccentBrush.Color.R);
            Assert.Equal(0xB6, customAccentBrush.Color.G);
            Assert.Equal(0xD4, customAccentBrush.Color.B);
        });
    }

    [Fact]
    public void InlineSettingsView_LoadAndChangeTheme_HandlesWhiteAndCustomAndGlass()
    {
        RunOnSta(() =>
        {
            var settingsView = new InlineSettingsView();
            var settings = new AppSettings
            {
                Theme = AppTheme.White,
                CustomAccentColor = "#10B981",
                CustomBackgroundColor = "#0A1F14"
            };

            settingsView.LoadSettings(settings);
            Assert.True(settingsView.ThemeWhiteRadio.IsChecked);
            Assert.Equal("#10B981", settingsView.AccentHexInput.Text);

            // Change to Liquid Glass
            settings.Theme = AppTheme.LiquidGlass;
            settingsView.LoadSettings(settings);
            Assert.True(settingsView.ThemeGlassRadio.IsChecked);

            // Change to Custom Theme
            settings.Theme = AppTheme.Custom;
            settingsView.LoadSettings(settings);
            Assert.True(settingsView.ThemeCustomRadio.IsChecked);
            Assert.Equal(Visibility.Visible, settingsView.CustomThemeSettingsPanel.Visibility);
            Assert.Equal("#0A1F14", settingsView.BgHexInput.Text);
        });
    }
}
