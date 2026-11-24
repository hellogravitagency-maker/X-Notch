using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using XNotch.Helpers;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace XNotch.Views;

public enum WallpaperWhy { Playing, NoVideo, Covered, Fullscreen }

/// <summary>
/// Looping, muted video drawn behind the desktop icons on the primary screen.
/// Uses the WorkerW layer Explorer creates for wallpaper transitions (message 0x052C).
/// Pauses whenever the desktop can't be seen; the normal wallpaper is restored on close.
/// </summary>
public sealed class LiveWallpaperWindow : Window
{
    private readonly MediaElement _video = new()
    {
        LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
        IsMuted = true, Stretch = Stretch.UniformToFill,
    };
    private readonly Rectangle _dim = new() { Fill = Brushes.Black, IsHitTestVisible = false };
    private readonly DispatcherTimer _coverCheck = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _paused = true;
    private IntPtr _worker;

    public WallpaperWhy Why { get; private set; } = WallpaperWhy.NoVideo;
    public Func<bool>? IsFullscreen { get; set; }

    public LiveWallpaperWindow(string file, double brightness)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Content = new Grid { Children = { _video, _dim } };
        SetBrightness(brightness);

        _video.MediaEnded += (_, _) => { _video.Position = TimeSpan.Zero; if (!_paused) _video.Play(); };
        _video.MediaFailed += (_, _) => Why = WallpaperWhy.NoVideo;
        if (File.Exists(file)) _video.Source = new Uri(file);

        SourceInitialized += (_, _) => Attach();
        Loaded += (_, _) => { Evaluate(); _coverCheck.Start(); };
        // ponytail: 2 s check for "a maximized window hides the desktop" (one Win32 call).
        // Upgrade path: EVENT_OBJECT_LOCATIONCHANGE hook filtered to top-level windows.
        _coverCheck.Tick += (_, _) => Evaluate();
        Closed += (_, _) => { _coverCheck.Stop(); _video.Close(); RepaintDesktop(); };
    }

    public void SetBrightness(double b) => _dim.Opacity = 1 - Math.Clamp(b, 0.2, 1);

    /// <summary>Re-checks whether the desktop is visible and plays or pauses (call on focus/fullscreen changes too).</summary>
    public void Evaluate()
    {
        if (_video.Source == null) { Why = WallpaperWhy.NoVideo; return; }
        Why = IsFullscreen?.Invoke() == true ? WallpaperWhy.Fullscreen
            : MaximizedOnPrimary() ? WallpaperWhy.Covered
            : WallpaperWhy.Playing;
        bool play = Why == WallpaperWhy.Playing;
        if (play == !_paused) return;
        _paused = !play;
        if (play) _video.Play(); else _video.Pause(); // paused video costs no GPU decode
    }

    private void Attach()
    {
        var worker = _worker = FindWallpaperLayer();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (worker != IntPtr.Zero) SetParent(hwnd, worker);
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var origin = new POINT { X = screen.Left, Y = screen.Top };
        if (worker != IntPtr.Zero) ScreenToClient(worker, ref origin); // child coordinates are relative to WorkerW
        SetWindowPos(hwnd, IntPtr.Zero, origin.X, origin.Y, screen.Width, screen.Height, 0x0010 /* NOACTIVATE */ | 0x0004 /* NOZORDER */);
    }

    /// <summary>
    /// Asks Progman to spawn the WorkerW that sits between the wallpaper and the icons.
    /// Windows 11 24H2+: WorkerW is a child of Progman. Older: a top-level WorkerW right
    /// after the one that hosts SHELLDLL_DefView.
    /// </summary>
    public static IntPtr FindWallpaperLayer()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        var child = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (child != IntPtr.Zero) return child;

        IntPtr found = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                found = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }

    private static bool MaximizedOnPrimary()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || !IsZoomed(fg)) return false;
        return MonitorFromWindow(fg, 2) == MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1 /* PRIMARY */);
    }

    /// <summary>
    /// Asks Explorer's wallpaper layer to repaint over our last frame. Deliberately does not
    /// re-set the wallpaper (that would break slideshow / Spotlight wallpapers).
    /// </summary>
    private void RepaintDesktop()
    {
        if (_worker != IntPtr.Zero)
            RedrawWindow(_worker, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x100); // INVALIDATE | ERASE | ALLCHILDREN | UPDATENOW
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr region, uint flags);
}
