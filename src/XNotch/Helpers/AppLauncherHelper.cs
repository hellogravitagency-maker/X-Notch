using System;
using System.Diagnostics;
using System.Linq;
using XNotch.Models;

namespace XNotch.Helpers;

/// <summary>
/// Opens, activates, or brings to the foreground the target application associated
/// with a notification or operating mode alert.
/// </summary>
public static class AppLauncherHelper
{
    public static bool LaunchApplicationForNotification(NotificationItem item)
    {
        string app = (item.AppName ?? "").Trim().ToLowerInvariant();
        string title = (item.Title ?? "").Trim().ToLowerInvariant();
        string aumid = (item.AppUserModelId ?? "").Trim();

        // 1. Operating Mode / System Power notification
        if (app.Contains("operating mode") || app.Contains("power") || title.Contains("mode"))
        {
            return LaunchPowerApplication();
        }

        // 2. Try AppUserModelId (for UWP / Windows Store packaged apps)
        if (!string.IsNullOrWhiteSpace(aumid))
        {
            try
            {
                Process.Start(new ProcessStartInfo($"shell:AppsFolder\\{aumid}") { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        // 3. Try focusing an already-running process
        if (!string.IsNullOrEmpty(item.AppName) && TryFocusRunningProcess(item.AppName))
        {
            return true;
        }

        // 4. Try protocol / common URI
        if (TryLaunchProtocol(app))
        {
            return true;
        }

        // 5. Try launching executable
        if (!string.IsNullOrEmpty(item.AppName) && TryLaunchExecutable(item.AppName))
        {
            return true;
        }

        return false;
    }

    public static bool LaunchPowerApplication()
    {
        // Try Armoury Crate if installed on ASUS ROG/TUF machines
        try
        {
            var p = Process.Start(new ProcessStartInfo("armoury-crate:") { UseShellExecute = true });
            if (p != null) return true;
        }
        catch { }

        // Fallback: Windows Power & Battery Settings (standard on Windows 10/11)
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true });
            return true;
        }
        catch
        {
            try
            {
                // Fallback 2: Windows Mobility Center
                Process.Start(new ProcessStartInfo("mblctr.exe") { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        return false;
    }

    private static bool TryFocusRunningProcess(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return false;

        string cleanName = appName.Replace(" ", "").ToLowerInvariant();

        try
        {
            var procs = Process.GetProcesses();
            foreach (var proc in procs)
            {
                try
                {
                    string pName = proc.ProcessName.ToLowerInvariant();
                    string wTitle = proc.MainWindowTitle.ToLowerInvariant();

                    if (pName.Contains(cleanName) || cleanName.Contains(pName) ||
                        (!string.IsNullOrWhiteSpace(wTitle) && wTitle.Contains(cleanName)))
                    {
                        if (TerminalFocusHelper.FocusProcessWindow(proc.Id))
                        {
                            return true;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    private static bool TryLaunchProtocol(string app)
    {
        string? protocol = app switch
        {
            _ when app.Contains("spotify") => "spotify:",
            _ when app.Contains("slack") => "slack:",
            _ when app.Contains("discord") => "discord:",
            _ when app.Contains("whatsapp") => "whatsapp:",
            _ when app.Contains("telegram") => "tg:",
            _ when app.Contains("teams") => "msteams:",
            _ when app.Contains("outlook") || app.Contains("mail") => "mailto:",
            _ when app.Contains("settings") => "ms-settings:",
            _ => null
        };

        if (protocol != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(protocol) { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        return false;
    }

    private static bool TryLaunchExecutable(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return false;

        string exeName = appName.ToLowerInvariant() switch
        {
            _ when appName.Contains("chrome", StringComparison.OrdinalIgnoreCase) => "chrome.exe",
            _ when appName.Contains("edge", StringComparison.OrdinalIgnoreCase) => "msedge.exe",
            _ when appName.Contains("firefox", StringComparison.OrdinalIgnoreCase) => "firefox.exe",
            _ when appName.Contains("code", StringComparison.OrdinalIgnoreCase) || appName.Contains("visual studio code", StringComparison.OrdinalIgnoreCase) => "code.exe",
            _ when appName.Contains("notepad", StringComparison.OrdinalIgnoreCase) => "notepad.exe",
            _ when appName.Contains("terminal", StringComparison.OrdinalIgnoreCase) => "wt.exe",
            _ when appName.Contains("explorer", StringComparison.OrdinalIgnoreCase) || appName.Contains("files", StringComparison.OrdinalIgnoreCase) => "explorer.exe",
            _ => appName.Trim() + ".exe"
        };

        try
        {
            Process.Start(new ProcessStartInfo(exeName) { UseShellExecute = true });
            return true;
        }
        catch { }

        return false;
    }
}
