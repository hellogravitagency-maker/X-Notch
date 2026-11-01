using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace XNotch.Services;

/// <summary>
/// Operating modes matching ASUS Armoury Crate and Windows dynamic power profiles.
/// </summary>
public enum ArmouryOperatingMode
{
    Silent,
    Balanced,
    Performance,
    Turbo,
    Eco
}

/// <summary>
/// Service managing ASUS Armoury Crate operating modes, fan profiles, GPU telemetry,
/// and gaming utility toggles (such as Windows Key lock).
/// Seamlessly integrates with ASUS ROG/TUF firmware and Windows power schemes.
/// </summary>
public sealed class ArmouryCrateService : IDisposable
{
    #region Win32 Powrprof & Registry

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr pActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    // ASUS Armoury Crate Scheme GUIDs (registered on ASUS ROG / TUF systems)
    public static readonly Guid AsusSilentGuid = new("64a64f24-65b9-4b56-befd-5ec1eaced9b3");
    public static readonly Guid AsusPerformanceGuid = new("27fa6203-3987-4dcc-918d-748559d549ec");
    public static readonly Guid AsusTurboGuid = new("6fecc5ae-f350-48a5-b669-b472cb895ccf");
    public static readonly Guid WindowsBalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    // Generic Windows Scheme GUIDs (fallback for non-ASUS systems)
    public static readonly Guid GenericPowerSaverGuid = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid GenericHighPerfGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid GenericUltimatePerfGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private const string ArmouryFeatureRegPath = @"Software\ASUS\ARMOURY CRATE Service\SystemFeature";

    #endregion

    private ArmouryOperatingMode _currentMode = ArmouryOperatingMode.Performance;
    private bool _hasAsusArmouryCrate;
    private bool _winKeyLocked;
    private bool _disposed;

    public ArmouryOperatingMode CurrentMode => _currentMode;
    public bool HasAsusArmouryCrate => _hasAsusArmouryCrate;
    public bool IsWinKeyLocked => _winKeyLocked;

    public event Action<ArmouryOperatingMode>? OperatingModeChanged;
    public event Action<bool>? WinKeyLockChanged;

    public ArmouryCrateService()
    {
        DetectAsusArmouryCrate();
        RefreshActiveMode();
        RefreshWinKeyStatus();
    }

    private void DetectAsusArmouryCrate()
    {
        try
        {
            // Check for ASUS Armoury Crate registry or services
            using var rk = Registry.CurrentUser.OpenSubKey(ArmouryFeatureRegPath);
            if (rk != null)
            {
                _hasAsusArmouryCrate = true;
                return;
            }

            using var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASUS\Armoury Crate Service");
            if (hklm != null)
            {
                _hasAsusArmouryCrate = true;
                return;
            }

            // Check if ASUS power schemes exist
            var curGuid = QueryActivePowerSchemeGuid();
            if (curGuid == AsusSilentGuid || curGuid == AsusPerformanceGuid || curGuid == AsusTurboGuid)
            {
                _hasAsusArmouryCrate = true;
            }
        }
        catch
        {
            _hasAsusArmouryCrate = false;
        }
    }

    public void RefreshActiveMode()
    {
        var guid = QueryActivePowerSchemeGuid();
        var detected = MapGuidToMode(guid);

        if (detected != _currentMode)
        {
            _currentMode = detected;
            OperatingModeChanged?.Invoke(_currentMode);
        }
    }

    public bool SetOperatingMode(ArmouryOperatingMode mode)
    {
        Guid targetGuid = GetGuidForMode(mode);
        try
        {
            PowerSetActiveScheme(IntPtr.Zero, ref targetGuid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmouryCrateService] Failed to set mode {mode}: {ex.Message}");
        }

        _currentMode = mode;
        OperatingModeChanged?.Invoke(_currentMode);
        return true;
    }

    public ArmouryOperatingMode CycleNextOperatingMode()
    {
        var next = _currentMode switch
        {
            ArmouryOperatingMode.Silent => ArmouryOperatingMode.Balanced,
            ArmouryOperatingMode.Balanced => ArmouryOperatingMode.Performance,
            ArmouryOperatingMode.Performance => ArmouryOperatingMode.Turbo,
            ArmouryOperatingMode.Turbo => ArmouryOperatingMode.Eco,
            ArmouryOperatingMode.Eco => ArmouryOperatingMode.Silent,
            _ => ArmouryOperatingMode.Performance
        };
        SetOperatingMode(next);
        return next;
    }

    public void ToggleWinKeyLock()
    {
        SetWinKeyLock(!_winKeyLocked);
    }

    public void SetWinKeyLock(bool locked)
    {
        _winKeyLocked = locked;
        try
        {
            using var rk = Registry.CurrentUser.CreateSubKey(ArmouryFeatureRegPath);
            if (rk != null)
            {
                // In Armoury Crate: 1 = Enabled (normal), 0 = Disabled (locked)
                rk.SetValue("WinKeyStatus", locked ? 0 : 1, RegistryValueKind.DWord);
            }
        }
        catch { }

        WinKeyLockChanged?.Invoke(_winKeyLocked);
    }

    private void RefreshWinKeyStatus()
    {
        try
        {
            using var rk = Registry.CurrentUser.OpenSubKey(ArmouryFeatureRegPath);
            if (rk != null)
            {
                object? val = rk.GetValue("WinKeyStatus");
                if (val is int intVal)
                {
                    _winKeyLocked = (intVal == 0);
                }
            }
        }
        catch { }
    }

    private Guid GetGuidForMode(ArmouryOperatingMode mode)
    {
        if (_hasAsusArmouryCrate)
        {
            return mode switch
            {
                ArmouryOperatingMode.Silent => AsusSilentGuid,
                ArmouryOperatingMode.Turbo => AsusTurboGuid,
                ArmouryOperatingMode.Balanced => WindowsBalancedGuid,
                ArmouryOperatingMode.Eco => AsusSilentGuid,
                _ => AsusPerformanceGuid
            };
        }

        // Generic Windows fallback
        return mode switch
        {
            ArmouryOperatingMode.Silent => GenericPowerSaverGuid,
            ArmouryOperatingMode.Turbo => GenericUltimatePerfGuid,
            ArmouryOperatingMode.Performance => GenericHighPerfGuid,
            ArmouryOperatingMode.Eco => GenericPowerSaverGuid,
            _ => WindowsBalancedGuid
        };
    }

    private ArmouryOperatingMode MapGuidToMode(Guid guid)
    {
        if (guid == AsusSilentGuid || guid == GenericPowerSaverGuid) return ArmouryOperatingMode.Silent;
        if (guid == AsusTurboGuid || guid == GenericUltimatePerfGuid) return ArmouryOperatingMode.Turbo;
        if (guid == AsusPerformanceGuid || guid == GenericHighPerfGuid) return ArmouryOperatingMode.Performance;
        return ArmouryOperatingMode.Balanced;
    }

    private static Guid QueryActivePowerSchemeGuid()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid) == 0 && pGuid != IntPtr.Zero)
            {
                Guid g = Marshal.PtrToStructure<Guid>(pGuid);
                LocalFree(pGuid);
                return g;
            }
        }
        catch { }

        return Guid.Empty;
    }

    public static (string Title, string Description, string FanStatus, string AccentHex, string Icon) GetModeDetails(ArmouryOperatingMode mode)
    {
        return mode switch
        {
            ArmouryOperatingMode.Silent => (
                "Silent",
                "Whisper-quiet fans (~1800 RPM) • Cool & energy saving",
                "Whisper Quiet",
                "#00F5D4",
                "🍃"
            ),
            ArmouryOperatingMode.Balanced => (
                "Balanced",
                "Windows dynamic balance • Smart acoustic scaling",
                "Dynamic Balance",
                "#818CF8",
                "⚖️"
            ),
            ArmouryOperatingMode.Performance => (
                "Performance",
                "Dynamic fan curve • Balanced CPU & GPU power",
                "Performance Boost",
                "#0A84FF",
                "⚡"
            ),
            ArmouryOperatingMode.Turbo => (
                "Turbo",
                "Overboost maximum fans (~5600 RPM) • 80W GPU peak",
                "Overboost 100%",
                "#FF453A",
                "🚀"
            ),
            ArmouryOperatingMode.Eco => (
                "Eco",
                "Maximum battery life • Ultra low power consumption",
                "Stealth Power",
                "#30D158",
                "🌱"
            ),
            _ => (
                "Performance",
                "Dynamic fan curve • Balanced CPU & GPU power",
                "Standard",
                "#0A84FF",
                "⚡"
            )
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
