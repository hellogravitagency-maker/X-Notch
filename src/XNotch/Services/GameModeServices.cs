using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Timer = System.Threading.Timer;

namespace XNotch.Services;

/// <summary>What the island does while an app is in front (NotchPixel's AppBehaviour).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppRule { Normal, Slim, Hide, Game }

public static class AppRules
{
    /// <summary>
    /// The per-app rule wins; otherwise a fullscreen app on this island's screen gets the
    /// fullscreen rule; otherwise Normal.
    /// </summary>
    public static AppRule Resolve(IReadOnlyDictionary<string, AppRule> rules, string? foregroundExe, bool fullscreenHere, AppRule fullscreenRule)
    {
        if (foregroundExe != null && rules.TryGetValue(foregroundExe.ToLowerInvariant(), out var r)) return r;
        return fullscreenHere ? fullscreenRule : AppRule.Normal;
    }
}

/// <summary>
/// Foreground app tracking via EVENT_SYSTEM_FOREGROUND (event-driven, no polling).
/// Must be started on a thread with a message loop (the UI thread).
/// </summary>
public sealed class ForegroundAppService : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private IntPtr _hook;
    private WinEventProc? _proc; // keep the delegate alive
    private readonly LinkedList<string> _recent = new();

    public string? ExeName { get; private set; }
    public int ProcessId { get; private set; }
    public IntPtr Window { get; private set; }

    /// <summary>Distinct recently-focused apps, newest first (for the rules list).</summary>
    public IReadOnlyList<string> Recent => _recent.ToList();

    public event Action? Changed;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = OnForeground;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        OnForeground(IntPtr.Zero, 0, GetForegroundWindow(), 0, 0, 0, 0);
    }

    private void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out int pid);
        if (pid == Environment.ProcessId) return; // clicking the island must not change the rule
        string? exe = ExeOf(pid);
        if (exe == null) return;

        Window = hwnd;
        ProcessId = pid;
        if (exe != ExeName)
        {
            ExeName = exe;
            if (exe != "explorer.exe")
            {
                _recent.Remove(exe);
                _recent.AddFirst(exe);
                while (_recent.Count > 12) _recent.RemoveLast();
            }
        }
        Changed?.Invoke();
    }

    private static string? ExeOf(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? Path.GetFileName(sb.ToString()).ToLowerInvariant() : null;
        }
        finally { CloseHandle(h); }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
}

public enum FpsState { Off, Running, NeedsPermission, Unavailable }

/// <summary>
/// Frames per second per process, counted from DXGI Present events over ETW (the same
/// signal PresentMon uses). Covers DirectX 10/11/12 games; Vulkan/OpenGL show no FPS.
/// Real-time ETW needs admin or membership of "Performance Log Users".
/// Runs only while the game strip is visible.
/// </summary>
public sealed class FpsService : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int PresentStart = 42, PresentMultiplaneOverlayStart = 55;

    private TraceEventSession? _session;
    private Thread? _thread;
    private Timer? _window;
    private ConcurrentDictionary<int, int> _counting = new();
    private volatile IReadOnlyDictionary<int, int> _last = new Dictionary<int, int>();

    public FpsState State { get; private set; } = FpsState.Off;

    public int? FpsOf(int pid) => _last.TryGetValue(pid, out var f) ? f : null;

    public void Start()
    {
        if (_session != null) return;
        try
        {
            _session = new TraceEventSession("XNotch-Fps") { StopOnDispose = true };
            _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational);
            _session.Source.AllEvents += OnEvent;
            _thread = new Thread(() => { try { _session?.Source.Process(); } catch { } }) { IsBackground = true, Name = "XNotch.Fps" };
            _thread.Start();
            _window = new Timer(_ => _last = Interlocked.Exchange(ref _counting, new()), null, 1000, 1000);
            State = FpsState.Running;
        }
        catch (UnauthorizedAccessException) { Cleanup(); State = FpsState.NeedsPermission; }
        catch (Exception ex) { Debug.WriteLine($"FPS: {ex.Message}"); Cleanup(); State = FpsState.Unavailable; }
    }

    private void OnEvent(TraceEvent e)
    {
        if (e.ProviderGuid != DxgiProvider) return;
        int id = (int)e.ID;
        if (id == PresentStart || id == PresentMultiplaneOverlayStart)
            _counting.AddOrUpdate(e.ProcessID, 1, (_, c) => c + 1);
    }

    public void Stop()
    {
        Cleanup();
        State = FpsState.Off;
        _last = new Dictionary<int, int>();
    }

    private void Cleanup()
    {
        _window?.Dispose();
        _window = null;
        try { _session?.Dispose(); } catch { }
        _session = null;
        _thread = null;
    }

    public void Dispose() => Stop();
}
