using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace XNotch.Services;

/// <summary>
/// Monitors system-wide camera and microphone hardware usage by inspecting
/// Windows CapabilityAccessManager ConsentStore records and WASAPI audio capture sessions.
/// Displays genuine Apple Dynamic Island green (camera) and amber (microphone) privacy dots.
/// </summary>
public sealed class PrivacyIndicatorService : IDisposable
{
    private const string WebcamConsentPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private const string MicConsentPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private System.Threading.Timer? _pollTimer;
    private MMDeviceEnumerator? _deviceEnumerator;
    private bool _disposed;
    private int _isChecking;

    public bool IsCameraActive { get; private set; }
    public string? CameraActiveApp { get; private set; }

    public bool IsMicrophoneActive { get; private set; }
    public string? MicrophoneActiveApp { get; private set; }

    public event Action<bool, string?>? CameraStateChanged;
    public event Action<bool, string?>? MicrophoneStateChanged;

    public void Initialize()
    {
        // Initial check immediately
        Task.Run(CheckPrivacyStatus);

        // Poll every 1200ms in background thread
        _pollTimer = new System.Threading.Timer(_ =>
        {
            if (!_disposed)
                CheckPrivacyStatus();
        }, null, 1200, 1200);
    }

    public void CheckPrivacyStatus()
    {
        if (Interlocked.Exchange(ref _isChecking, 1) == 1)
            return;

        try
        {
            // 1. Check Camera
            var (camActive, camApp) = CheckCapability(WebcamConsentPath);
            if (camActive != IsCameraActive || camApp != CameraActiveApp)
            {
                IsCameraActive = camActive;
                CameraActiveApp = camApp;
                CameraStateChanged?.Invoke(camActive, camApp);
            }

            // 2. Check Microphone
            var (micActive, micApp) = CheckCapability(MicConsentPath);

            // Also cross-verify with WASAPI active capture sessions if registry doesn't show active
            if (!micActive)
            {
                var (wasapiActive, wasapiApp) = CheckWasapiCaptureSessions();
                if (wasapiActive)
                {
                    micActive = true;
                    micApp = wasapiApp ?? "Audio Capture";
                }
            }

            if (micActive != IsMicrophoneActive || micApp != MicrophoneActiveApp)
            {
                IsMicrophoneActive = micActive;
                MicrophoneActiveApp = micApp;
                MicrophoneStateChanged?.Invoke(micActive, micApp);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PrivacyService] Error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    private static (bool Active, string? AppName) CheckCapability(string subKeyPath)
    {
        try
        {
            using var baseKey = Registry.CurrentUser.OpenSubKey(subKeyPath);
            if (baseKey == null) return (false, null);

            // 1. Check NonPackaged Win32 apps (e.g. Zoom, Teams, Chrome, OBS)
            using (var nonPackagedKey = baseKey.OpenSubKey("NonPackaged"))
            {
                if (nonPackagedKey != null)
                {
                    foreach (var appSubKeyName in nonPackagedKey.GetSubKeyNames())
                    {
                        using var appKey = nonPackagedKey.OpenSubKey(appSubKeyName);
                        if (appKey == null) continue;

                        if (IsKeyActive(appKey))
                        {
                            string appName = CleanAppNameFromPath(appSubKeyName);
                            return (true, appName);
                        }
                    }
                }
            }

            // 2. Check Packaged UWP/MSIX apps
            foreach (var appSubKeyName in baseKey.GetSubKeyNames())
            {
                if (appSubKeyName.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var appKey = baseKey.OpenSubKey(appSubKeyName);
                if (appKey == null) continue;

                if (IsKeyActive(appKey))
                {
                    string appName = CleanPackagedAppName(appSubKeyName);
                    return (true, appName);
                }
            }
        }
        catch
        {
            // Silently swallow registry reading issues
        }

        return (false, null);
    }

    private static bool IsKeyActive(RegistryKey key)
    {
        var stopVal = key.GetValue("LastUsedTimeStop");
        var startVal = key.GetValue("LastUsedTimeStart");

        if (startVal is long start && start > 0)
        {
            if (stopVal == null || (stopVal is long stop && (stop == 0 || stop < start)))
            {
                return true;
            }
        }
        return false;
    }

    private static string CleanAppNameFromPath(string raw)
    {
        // e.g. "C:#Users#mthej#AppData#Roaming#Zoom#bin#Zoom.exe"
        string path = raw.Replace('#', '\\');
        try
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;
        }
        catch { }
        return raw;
    }

    private static string CleanPackagedAppName(string raw)
    {
        // e.g. "Microsoft.GamingApp_8wekyb3d8bbwe" -> "GamingApp"
        int firstDot = raw.IndexOf('.');
        int underscore = raw.IndexOf('_');
        if (firstDot >= 0 && underscore > firstDot)
        {
            return raw.Substring(firstDot + 1, underscore - firstDot - 1);
        }
        if (underscore > 0)
        {
            return raw.Substring(0, underscore);
        }
        return raw;
    }

    private (bool Active, string? AppName) CheckWasapiCaptureSessions()
    {
        try
        {
            _deviceEnumerator ??= new MMDeviceEnumerator();
            using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            if (device != null)
            {
                var mgr = device.AudioSessionManager;
                if (mgr != null)
                {
                    var count = mgr.Sessions.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var session = mgr.Sessions[i];
                        if (session.State == AudioSessionState.AudioSessionStateActive)
                        {
                            string? procName = null;
                            try
                            {
                                uint pid = session.GetProcessID;
                                if (pid > 0)
                                {
                                    using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                                    procName = proc.ProcessName;
                                }
                            }
                            catch { }

                            return (true, procName ?? "Microphone");
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully if audio endpoint is disconnected
        }

        return (false, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        _deviceEnumerator?.Dispose();
    }
}
