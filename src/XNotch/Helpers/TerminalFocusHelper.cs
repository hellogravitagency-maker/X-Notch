using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XNotch.Helpers;

/// <summary>
/// Brings the exact window or host Windows Terminal hosting an active coding agent to the foreground.
/// </summary>
public static class TerminalFocusHelper
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    public static bool FocusProcessWindow(int processId)
    {
        try
        {
            var proc = Process.GetProcessById(processId);
            IntPtr hWnd = proc.MainWindowHandle;

            // If main window handle is zero (e.g., node process inside Windows Terminal), find parent
            if (hWnd == IntPtr.Zero)
            {
                var parent = FindParentProcess(proc);
                if (parent != null && parent.MainWindowHandle != IntPtr.Zero)
                {
                    hWnd = parent.MainWindowHandle;
                }
            }

            if (hWnd != IntPtr.Zero)
            {
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                return SetForegroundWindow(hWnd);
            }
        }
        catch { }

        return false;
    }

    private static Process? FindParentProcess(Process process)
    {
        try
        {
            // Windows Terminal processes: WindowsTerminal.exe or wt.exe
            var terminals = Process.GetProcessesByName("WindowsTerminal");
            if (terminals.Length > 0)
                return terminals[0];

            var openConsoles = Process.GetProcessesByName("OpenConsole");
            if (openConsoles.Length > 0)
                return openConsoles[0];
        }
        catch { }

        return null;
    }
}
