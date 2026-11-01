using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace XNotch.Services;

/// <summary>
/// Detects when another application is running in fullscreen mode.
/// Used to auto-hide the notch when a fullscreen app is active.
/// </summary>
public class FullscreenService : IDisposable
{
    private DispatcherTimer? _timer;

    /// <summary>Whether a fullscreen application is currently detected.</summary>
    public bool IsFullscreen { get; private set; }

    /// <summary>Monitor handle hosting the fullscreen app (IntPtr.Zero when none).</summary>
    public IntPtr FullscreenMonitor { get; private set; }

    /// <summary>Fired when fullscreen state changes. Args: isFullscreen</summary>
    public event Action<bool>? FullscreenChanged;

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
    }

    // Consecutive non-fullscreen checks required before showing the notch again.
    // Overlays, toasts, and alt-tab previews briefly steal the foreground while a
    // game runs fullscreen; without this debounce the notch flashes into view.
    private const int ClearStreakRequired = 3;
    private int _clearStreak;

    private void Check()
    {
        bool fullscreen = IsFullscreenAppRunning(out var monitor);

        if (fullscreen)
        {
            _clearStreak = 0;
            if (!IsFullscreen || monitor != FullscreenMonitor)
            {
                IsFullscreen = true;
                FullscreenMonitor = monitor;
                FullscreenChanged?.Invoke(true);
            }
        }
        else if (IsFullscreen && ++_clearStreak >= ClearStreakRequired)
        {
            IsFullscreen = false;
            FullscreenMonitor = IntPtr.Zero;
            FullscreenChanged?.Invoke(false);
        }
    }

    private static IntPtr _lastForeground;
    private static RECT _lastRect;
    private static bool _lastResult;
    private static IntPtr _lastMonitor;
    private static readonly System.Text.StringBuilder _classNameBuffer = new(64);
    private static readonly object _syncLock = new();

    private static bool IsFullscreenAppRunning(out IntPtr fullscreenMonitor)
    {
        fullscreenMonitor = IntPtr.Zero;
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        // Ignore the desktop/shell window
        IntPtr desktop = GetDesktopWindow();
        IntPtr shell = GetShellWindow();
        if (foreground == desktop || foreground == shell) return false;

        lock (_syncLock)
        {
            if (GetWindowRect(foreground, out RECT windowRect))
            {
                // Quick return if foreground window and its geometry haven't changed
                if (foreground == _lastForeground &&
                    windowRect.Left == _lastRect.Left &&
                    windowRect.Top == _lastRect.Top &&
                    windowRect.Right == _lastRect.Right &&
                    windowRect.Bottom == _lastRect.Bottom)
                {
                    fullscreenMonitor = _lastMonitor;
                    return _lastResult;
                }

                _lastForeground = foreground;
                _lastRect = windowRect;

                _classNameBuffer.Clear();
                if (GetClassName(foreground, _classNameBuffer, _classNameBuffer.Capacity) > 0)
                {
                    string cls = _classNameBuffer.ToString();
                    if (cls is "Progman" or "WorkerW")
                    {
                        _lastResult = false;
                        _lastMonitor = IntPtr.Zero;
                        return false;
                    }
                }

                // Get the monitor info for the foreground window
                IntPtr monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var screen = monitorInfo.rcMonitor;
                    bool covers = windowRect.Left <= screen.Left &&
                                  windowRect.Top <= screen.Top &&
                                  windowRect.Right >= screen.Right &&
                                  windowRect.Bottom >= screen.Bottom;
                    if (covers)
                    {
                        fullscreenMonitor = monitor;
                        _lastMonitor = monitor;
                        _lastResult = true;
                        return true;
                    }
                }
            }

            _lastResult = false;
            _lastMonitor = IntPtr.Zero;
            return false;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
    }

    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    #endregion
}
