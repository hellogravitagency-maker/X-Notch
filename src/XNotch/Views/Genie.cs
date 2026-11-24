using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using XNotch.Helpers;
using XNotch.Models;
using DRect = System.Drawing.Rectangle;
using DPoint = System.Drawing.Point;

namespace XNotch.Views;

/// <summary>
/// macOS genie: the window is drawn as horizontal strips that funnel into its dock icon.
/// Strips are live DWM thumbnails, so it works for minimized, GPU-drawn and elevated windows
/// (DWM keeps the last frame of a minimized window). Pure math lives in <see cref="Layout"/>.
/// </summary>
public static class Genie
{
    public const int Strips = 48;
    public const double DurationMs = 520; // macOS genie is ~0.5 s; this is a rare, signature motion

    /// <summary>Strip <paramref name="i"/> at progress <paramref name="p"/> (0 = window, 1 = in the dock). Physical px.</summary>
    public static (double L, double T, double R, double B) Layout(DRect src, DRect dst, int i, double p)
    {
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // ease-in-out cubic
        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // Bottom edge dives first, the top follows: the window pours through the funnel.
        double yBot = Lerp(src.Bottom, dst.Bottom, Ease(Clamp01(p / 0.6)));
        double yTop = Lerp(src.Top, dst.Top, Ease(Clamp01((p - 0.3) / 0.7)));
        double y0 = Lerp(yTop, yBot, i / (double)Strips);
        double y1 = Lerp(yTop, yBot, (i + 1) / (double)Strips);

        // Funnel sides: an S-curve from full width at the window top to icon width at the dock.
        double span = Math.Max(1, dst.Bottom - src.Top);
        double u = Clamp01(((y0 + y1) / 2 - src.Top) / span);
        double f = (1 - Math.Cos(Math.PI * u)) / 2 * Ease(Clamp01(p / 0.45));
        f += (1 - f) * Ease(Clamp01((p - 0.6) / 0.4)); // every row lands exactly on the icon
        return (Lerp(src.Left, dst.Left, f), y0, Lerp(src.Right, dst.Right, f), y1 + 1); // +1 px hides seams
    }

    /// <summary>Icon-sized landing rect for a window, centred on the dock point.</summary>
    public static DRect Target(DRect src, DPoint dock, int iconPx)
    {
        int w = iconPx, h = Math.Max(4, (int)(iconPx * (src.Height / (double)Math.Max(1, src.Width))));
        return new DRect(dock.X - w / 2, dock.Y - h / 2, w, h);
    }

    /// <summary>Plays the funnel from <paramref name="source"/>'s live image. <paramref name="reverse"/> pours out of the dock.</summary>
    public static void Play(IntPtr source, DRect src, DRect dst, bool reverse, Action? done = null)
    {
        if (!SystemParameters.ClientAreaAnimation) { done?.Invoke(); return; } // Windows "Animation effects" off

        var union = DRect.Union(src, dst);
        var overlay = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, Topmost = true, ShowActivated = false, Title = "X-Notch Genie",
        };
        overlay.SourceInitialized += (_, _) =>
        {
            WindowHelper.MakeOverlayWindow(overlay);
            WindowHelper.SetClickThrough(overlay, true);
            WindowHelper.SetWindowRectPhysical(overlay, union.X, union.Y, union.Width, union.Height);
        };

        var thumbs = new IntPtr[Strips];
        int sw = 1, sh = 1;
        var clock = Stopwatch.StartNew();
        void Finish()
        {
            CompositionTarget.Rendering -= Frame;
            foreach (var t in thumbs) if (t != IntPtr.Zero) DwmUnregisterThumbnail(t);
            overlay.Close();
            done?.Invoke();
        }
        void Frame(object? s, EventArgs e)
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / DurationMs);
            double p = reverse ? 1 - t : t;
            byte alpha = (byte)(p > 0.85 ? 255 * (1 - p) / 0.15 : 255); // melts into the icon
            for (int i = 0; i < Strips; i++)
            {
                var (l, top, r, b) = Layout(src, dst, i, p);
                var props = new THUMB
                {
                    dwFlags = 0x1 | 0x2 | 0x4 | 0x8, // RECTDESTINATION | RECTSOURCE | OPACITY | VISIBLE
                    rcDestination = new RECT { left = (int)(l - union.X), top = (int)(top - union.Y), right = (int)Math.Ceiling(r - union.X), bottom = (int)Math.Ceiling(b - union.Y) },
                    rcSource = new RECT { left = 0, top = sh * i / Strips, right = sw, bottom = sh * (i + 1) / Strips },
                    opacity = alpha, fVisible = true,
                };
                DwmUpdateThumbnailProperties(thumbs[i], ref props);
            }
            if (t >= 1) Finish();
        }
        overlay.Loaded += (_, _) =>
        {
            var me = new WindowInteropHelper(overlay).Handle;
            for (int i = 0; i < Strips; i++)
            {
                int hr = DwmRegisterThumbnail(me, source, out thumbs[i]);
                if (hr != 0) { App.LogTrace($"Genie: thumbnail failed 0x{hr:X}"); Finish(); return; }
            }
            DwmQueryThumbnailSourceSize(thumbs[0], out var size);
            sw = Math.Max(1, size.cx); sh = Math.Max(1, size.cy);
            clock.Restart();
            CompositionTarget.Rendering += Frame;
        };
        overlay.Show();
    }

    /// <summary>Where the window sits when not minimized (screen px), even while it is iconic.</summary>
    public static DRect RestoredFrame(IntPtr hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref wp)) return DRect.Empty;
        var r = wp.rcNormalPosition;
        // Workspace coordinates: relative to the primary work area's origin.
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var rect = DRect.FromLTRB(r.left + work.Left, r.top + work.Top, r.right + work.Left, r.bottom + work.Top);
        if ((wp.flags & 0x2 /* WPF_RESTORETOMAXIMIZED */) != 0) rect = System.Windows.Forms.Screen.FromRectangle(rect).WorkingArea;
        return rect;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT min, max; public RECT rcNormalPosition; }
    [StructLayout(LayoutKind.Sequential)]
    private struct THUMB
    {
        public int dwFlags;
        public RECT rcDestination, rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMB props);
}

/// <summary>
/// Genie for every window: listens for minimize and plays the funnel into the app's dock icon.
/// Windows' own minimize animation has already started by the time the event arrives
/// (events are asynchronous), so the genie follows it rather than replacing it.
/// ponytail: no window hiding hacks; replacing Windows' animation would need SPI_SETANIMATION, a system setting.
/// </summary>
public sealed class GenieService : IDisposable
{
    private readonly Func<IntPtr, DPoint?> _dockPoint;
    private readonly Func<int> _iconPx;
    private readonly WinEventProc _proc; // kept alive: the hook calls into it
    private readonly IntPtr _hook;

    public static GenieService? Instance { get; private set; }
    public AppSettings Settings { get; set; }

    public GenieService(AppSettings settings, Func<IntPtr, DPoint?> dockPoint, Func<int> iconPx)
    {
        Settings = settings;
        _dockPoint = dockPoint;
        _iconPx = iconPx;
        _proc = OnEvent;
        _hook = SetWinEventHook(0x0016, 0x0016 /* EVENT_SYSTEM_MINIMIZESTART */, IntPtr.Zero, _proc, 0, 0, 0 /* OUTOFCONTEXT */);
        Instance = this;
    }

    private bool Wanted(IntPtr h)
    {
        if (h == IntPtr.Zero || GetAncestor(h, 2 /* GA_ROOT */) != h) return false;
        // Only real app windows: tool/overlay windows (island, dock, HUDs) and caption-less popups never genie.
        if ((GetWindowLongPtr(h, -20).ToInt64() & 0x80 /* WS_EX_TOOLWINDOW */) != 0) return false;
        if ((GetWindowLongPtr(h, -16).ToInt64() & 0x00C00000 /* WS_CAPTION */) == 0) return false;
        GetWindowThreadProcessId(h, out int pid);
        return Settings.GenieEffect && (pid == Environment.ProcessId || Settings.GenieAllWindows);
    }

    private void OnEvent(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || !Wanted(h)) return;
        var frame = Genie.RestoredFrame(h);
        if (frame.Width < 8 || frame.Height < 8) return;
        Genie.Play(h, frame, Genie.Target(frame, _dockPoint(h) ?? BottomCentre(frame), _iconPx()), reverse: false);
    }

    /// <summary>Dock click on a minimized window: pour it out of the icon, then restore. False = not handled.</summary>
    public bool Restore(IntPtr h)
    {
        if (!Wanted(h) || !SystemParameters.ClientAreaAnimation) return false;
        var frame = Genie.RestoredFrame(h);
        if (frame.Width < 8 || frame.Height < 8) return false;
        Genie.Play(h, frame, Genie.Target(frame, _dockPoint(h) ?? BottomCentre(frame), _iconPx()), reverse: true, () =>
        {
            ShowWindow(h, 9 /* SW_RESTORE */);
            SetForegroundWindow(h);
        });
        return true;
    }

    private static DPoint BottomCentre(DRect frame)
    {
        var b = System.Windows.Forms.Screen.FromRectangle(frame).Bounds;
        return new DPoint(b.Left + b.Width / 2, b.Bottom - 40);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        if (Instance == this) Instance = null;
    }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
}
