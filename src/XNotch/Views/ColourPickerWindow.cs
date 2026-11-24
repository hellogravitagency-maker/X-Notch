using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XNotch.Helpers;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace XNotch.Views;

/// <summary>
/// Screen colour picker. Freezes the whole virtual screen into a bitmap, shows it full-screen
/// with a magnifier loupe, and returns the colour under the click (Esc / right-click cancels).
/// </summary>
public sealed class ColourPickerWindow : Window
{
    private const int LoupeCells = 11, CellSize = 10;

    private readonly BitmapSource _frozen;
    private readonly byte[] _pixels;
    private readonly int _stride;
    private readonly System.Drawing.Rectangle _virtual;
    private readonly Canvas _canvas = new();
    private readonly Border _loupe;
    private readonly Image _zoom = new() { Width = LoupeCells * CellSize, Height = LoupeCells * CellSize };
    private readonly Border _swatch = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
    private readonly TextBlock _hex = new() { Foreground = Brushes.White, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    public Color? Picked { get; private set; }

    public ColourPickerWindow()
    {
        _virtual = System.Windows.Forms.SystemInformation.VirtualScreen;
        using (var bmp = new System.Drawing.Bitmap(_virtual.Width, _virtual.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(_virtual.Left, _virtual.Top, 0, 0, bmp.Size);
            var h = bmp.GetHbitmap();
            try { _frozen = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
            finally { DeleteObject(h); }
        }
        _frozen = new FormatConvertedBitmap(_frozen, PixelFormats.Bgra32, null, 0);
        _frozen.Freeze();
        _stride = _frozen.PixelWidth * 4;
        _pixels = new byte[_stride * _frozen.PixelHeight];
        _frozen.CopyPixels(_pixels, _stride, 0);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = new ImageBrush(_frozen) { Stretch = Stretch.Fill };

        RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
        _loupe = new Border
        {
            CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6), IsHitTestVisible = false,
            Child = new StackPanel
            {
                Children =
                {
                    new Grid { Children = { _zoom, CenterMarker() }, ClipToBounds = true },
                    new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { _swatch, _hex } },
                },
            },
        };
        _canvas.Children.Add(_loupe);
        Content = _canvas;

        SourceInitialized += (_, _) => WindowHelper.SetWindowRectPhysical(this, _virtual.Left, _virtual.Top, _virtual.Width, _virtual.Height);
        Loaded += (_, _) => { Activate(); Focus(); Update(); };
        MouseMove += (_, _) => Update();
        MouseLeftButtonUp += (_, _) => { Picked = ColourAtCursor(); Close(); };
        MouseRightButtonUp += (_, _) => Close();
        KeyDown += OnKey;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Enter) { Picked = ColourAtCursor(); Close(); }
    }

    private static UIElement CenterMarker() => new Border
    {
        Width = CellSize, Height = CellSize, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private (int X, int Y) CursorPixel()
    {
        GetCursorPos(out var p);
        return (Math.Clamp(p.X - _virtual.Left, 0, _frozen.PixelWidth - 1), Math.Clamp(p.Y - _virtual.Top, 0, _frozen.PixelHeight - 1));
    }

    private Color ColourAtCursor()
    {
        var (x, y) = CursorPixel();
        int i = y * _stride + x * 4;
        return Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i]);
    }

    private void Update()
    {
        var (x, y) = CursorPixel();
        int half = LoupeCells / 2;
        int cx = Math.Clamp(x - half, 0, _frozen.PixelWidth - LoupeCells);
        int cy = Math.Clamp(y - half, 0, _frozen.PixelHeight - LoupeCells);
        _zoom.Source = new CroppedBitmap(_frozen, new Int32Rect(cx, cy, LoupeCells, LoupeCells));

        var c = ColourAtCursor();
        _swatch.Background = new SolidColorBrush(c);
        _hex.Text = Hex(c);

        // Loupe sits below-right of the cursor and flips at screen edges.
        var pos = Mouse.GetPosition(this);
        double w = _loupe.ActualWidth > 0 ? _loupe.ActualWidth : 130, h = _loupe.ActualHeight > 0 ? _loupe.ActualHeight : 150;
        double left = pos.X + 24 + w > ActualWidth ? pos.X - 24 - w : pos.X + 24;
        double top = pos.Y + 24 + h > ActualHeight ? pos.Y - 24 - h : pos.Y + 24;
        Canvas.SetLeft(_loupe, left);
        Canvas.SetTop(_loupe, top);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
}
