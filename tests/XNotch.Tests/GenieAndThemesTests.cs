using System.Drawing;
using XNotch.Models;
using XNotch.Services;
using XNotch.Views;
using Xunit;

namespace XNotch.Tests;

public class GenieAndThemesTests
{
    private static readonly Rectangle Window = new(400, 100, 800, 600);
    private static readonly Rectangle Icon = Genie.Target(Window, new Point(960, 1040), 48);

    [Fact]
    public void Genie_StartsAsTheWindow_AndEndsInTheIcon()
    {
        for (int i = 0; i < Genie.Strips; i++)
        {
            var (l, _, r, _) = Genie.Layout(Window, Icon, i, 0);
            Assert.Equal(Window.Left, l, 3);
            Assert.Equal(Window.Right, r, 3);

            var (el, et, er, eb) = Genie.Layout(Window, Icon, i, 1);
            Assert.InRange(el, Icon.Left - 1, Icon.Right + 1);
            Assert.InRange(er, Icon.Left - 1, Icon.Right + 1);
            Assert.InRange(et, Icon.Top - 1, Icon.Bottom + 1);
            Assert.InRange(eb, Icon.Top - 1, Icon.Bottom + 2);
        }
    }

    [Fact]
    public void Genie_Midway_IsAFunnel_NarrowerNearTheDock()
    {
        var top = Genie.Layout(Window, Icon, 0, 0.5);
        var bottom = Genie.Layout(Window, Icon, Genie.Strips - 1, 0.5);
        Assert.True(bottom.R - bottom.L < top.R - top.L, "rows nearer the dock must be narrower");
        Assert.True(bottom.B > Window.Bottom, "the bottom edge leads into the dock");
    }

    [Fact]
    public void Accent_OverridesPalette_AndStaysReadable()
    {
        var p = ThemePalette.CreateOled().WithAccent(System.Windows.Media.Color.FromRgb(0xFF, 0x2D, 0x55));
        Assert.Equal(0xFF, p.Accent.R);
        Assert.Equal(p.Accent, p.FocusRing);
        Assert.Equal(System.Windows.Media.Colors.Black, p.Background);
    }

    /// <summary>Builds every Settings page under every theme. Set XNOTCH_SNAPSHOTS to a folder to also get PNGs.</summary>
    [Fact]
    public void SettingsWindow_BuildsEveryPage_InEveryTheme()
    {
        System.Exception? caught = null;
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null) { try { new System.Windows.Application(); } catch { } }
                var dir = System.Environment.GetEnvironmentVariable("XNOTCH_SNAPSHOTS");
                foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.LiquidGlass, AppTheme.Glass, AppTheme.Graphite, AppTheme.Oled })
                {
                    var s = new AppSettings { Theme = theme, AdaptToWindowsTheme = false, AccentColor = theme == AppTheme.Graphite ? null : "#007AFF" };
                    var ts = new ThemeService();
                    ts.Apply(s);
                    var w = new SettingsWindow(s, ts, _ => { }, () => { }, () => ["game.exe"]);
                    foreach (var page in new[] { "General", "Appearance", "Island", "Dock", "Cards", "Media & HUD", "Widgets", "Clipboard", "Capture", "Agents", "Game Mode", "Desktop", "Phone Deck", "Streaming", "Plugins", "About" })
                    {
                        w.ShowPage(page);
                        if (dir == null || page is not ("Appearance" or "Dock")) continue;
                        var root = (System.Windows.FrameworkElement)w.Content;
                        // Offscreen: the page fade never ticks, so jump to its end state.
                        var main = (System.Windows.Controls.DockPanel)((System.Windows.Controls.Grid)root).Children[1];
                        var body = (System.Windows.FrameworkElement)((System.Windows.Controls.ScrollViewer)main.Children[1]).Content;
                        body.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                        body.Opacity = 1;
                        body.RenderTransform = null;
                        var host = new System.Windows.Controls.Grid { Width = 880, Height = 640, Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x2B, 0x4F, 0x9E), System.Windows.Media.Color.FromRgb(0x9A, 0x4F, 0xA8), 30) };
                        w.Content = null;
                        host.Children.Add(root);
                        foreach (System.Windows.ResourceDictionary d in w.Resources.MergedDictionaries) host.Resources.MergedDictionaries.Add(d);
                        host.Measure(new System.Windows.Size(880, 640));
                        host.Arrange(new System.Windows.Rect(0, 0, 880, 640));
                        host.UpdateLayout();
                        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(880, 640, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bmp.Render(host);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                        using (var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"{theme}-{page.Replace(' ', '_')}.png"))) enc.Save(f);
                        host.Children.Clear();
                        w.Content = root;
                    }
                    w.Close();
                }
            }
            catch (System.Exception ex) { caught = ex; }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.Null(caught);
    }

    [Fact]
    public void NewThemes_SurviveSettingsRoundTrip()
    {
        var s = new AppSettings { Theme = AppTheme.Graphite, AdaptToWindowsTheme = false };
        Assert.Equal(AppTheme.Graphite, s.GetManualTheme());
        Assert.Equal(AppTheme.Graphite, s.GetRequestedTheme());
    }
}
