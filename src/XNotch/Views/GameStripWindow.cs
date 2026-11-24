using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XNotch.Helpers;
using XNotch.Services;
using Orientation = System.Windows.Controls.Orientation;

namespace XNotch.Views;

/// <summary>
/// Slim click-through strip shown instead of the island while a game rule is active:
/// FPS · CPU · GPU (+temp) · RAM · clock. Never takes focus or input from the game.
/// </summary>
public sealed class GameStripWindow : Window
{
    private readonly NotchServices _services;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Windows.Forms.Screen _screen;

    public GameStripWindow(NotchServices services, System.Windows.Forms.Screen screen)
    {
        _services = services;
        _screen = screen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0, 0, 0)),
            CornerRadius = new CornerRadius(0, 0, 10, 10),
            Padding = new Thickness(12, 3, 12, 4),
            Child = _row,
        };
        _tick.Tick += (_, _) => Refresh();
        SourceInitialized += (_, _) =>
        {
            WindowHelper.MakeOverlayWindow(this);
            WindowHelper.SetClickThrough(this, true);
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _tick.Start(); } else _tick.Stop(); };
        SizeChanged += (_, _) => Place();
    }

    private void Place()
    {
        double scale = ScreenHelper.GetScaleForScreen(_screen);
        var b = _screen.Bounds;
        int w = (int)Math.Round(ActualWidth * scale);
        WindowHelper.MoveWindowPhysical(this, b.Left + (b.Width - w) / 2, b.Top);
    }

    private void Refresh()
    {
        _row.Children.Clear();
        var hw = _services.Hardware.CurrentSnapshot;
        var fps = _services.Fps;

        string fpsText = fps.State switch
        {
            FpsState.Running => _services.Foreground.ProcessId is int pid && fps.FpsOf(pid) is int f ? $"{f} FPS" : "– FPS",
            FpsState.NeedsPermission => Loc.T("FPS needs admin"),
            _ => "",
        };
        if (fpsText != "") Add(fpsText, Color.FromRgb(0x30, 0xD1, 0x58), bold: true);
        if (hw != null)
        {
            Add($"CPU {hw.CpuUsagePercent:0}%", Load(hw.CpuUsagePercent));
            Add(hw.GpuTemperatureCelsius > 0 ? $"GPU {hw.GpuUsagePercent:0}% {hw.GpuTemperatureCelsius:0}°" : $"GPU {hw.GpuUsagePercent:0}%", Load(hw.GpuUsagePercent));
            Add($"RAM {hw.RamUsagePercent:0}%", Load(hw.RamUsagePercent));
        }
        Add(DateTime.Now.ToString("t"), Colors.White);
    }

    // Green / amber / red by load, so a glance is enough.
    private static Color Load(double pct) => pct >= 90 ? Color.FromRgb(0xFF, 0x45, 0x3A)
        : pct >= 70 ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Color.FromRgb(0xEB, 0xEB, 0xF5);

    private void Add(string text, Color color, bool bold = false)
    {
        if (_row.Children.Count > 0)
            _row.Children.Add(new TextBlock { Text = "·", Margin = new Thickness(7, 0, 7, 0), Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)) });
        _row.Children.Add(new TextBlock
        {
            Text = text, FontSize = 11.5, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold, Foreground = new SolidColorBrush(color),
        });
    }
}
