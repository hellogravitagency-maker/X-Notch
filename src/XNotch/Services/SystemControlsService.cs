using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;

namespace XNotch.Services;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Quick controls: Wi-Fi / Bluetooth radios, mic mute, Focus, default audio devices.
/// Output mute and battery come from <see cref="VolumeService"/> / <see cref="BatteryService"/>.
/// </summary>
public sealed class SystemControlsService : IDisposable
{
    private Radio? _wifi, _bluetooth;
    private readonly MMDeviceEnumerator _devices = new();
    private Windows.UI.Shell.FocusSessionManager? _focus;

    /// <summary>Fired (any thread) when a radio or Focus changes outside the app.</summary>
    public event Action? Changed;

    public bool HasWifi => _wifi != null;
    public bool HasBluetooth => _bluetooth != null;
    public bool WifiOn => _wifi?.State == RadioState.On;
    public bool BluetoothOn => _bluetooth?.State == RadioState.On;
    public bool HasFocus => _focus != null;
    public bool FocusOn => _focus?.IsFocusActive == true;

    public async Task InitializeAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
            {
                foreach (var r in await Radio.GetRadiosAsync())
                {
                    if (r.Kind == RadioKind.WiFi) _wifi ??= r;
                    else if (r.Kind == RadioKind.Bluetooth) _bluetooth ??= r;
                }
                if (_wifi != null) _wifi.StateChanged += (_, _) => Changed?.Invoke();
                if (_bluetooth != null) _bluetooth.StateChanged += (_, _) => Changed?.Invoke();
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Radios unavailable: {ex.Message}"); }

        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.UI.Shell.FocusSessionManager")
                && Windows.UI.Shell.FocusSessionManager.IsSupported)
            {
                _focus = Windows.UI.Shell.FocusSessionManager.GetDefault();
                _focus.IsFocusActiveChanged += (_, _) => Changed?.Invoke();
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Focus unavailable: {ex.Message}"); _focus = null; }
    }

    public async Task SetWifiAsync(bool on) { if (_wifi != null) await _wifi.SetStateAsync(on ? RadioState.On : RadioState.Off); }
    public async Task SetBluetoothAsync(bool on) { if (_bluetooth != null) await _bluetooth.SetStateAsync(on ? RadioState.On : RadioState.Off); }

    public void SetFocus(bool on)
    {
        if (_focus == null) { OpenSettings("ms-settings:focus"); return; }
        if (on) _focus.TryStartFocusSession(); else _focus.DeactivateFocus();
    }

    public bool MicMuted
    {
        get { try { return Mic()?.AudioEndpointVolume.Mute == true; } catch { return false; } }
        set { try { if (Mic() is { } m) m.AudioEndpointVolume.Mute = value; } catch { } }
    }

    private MMDevice? Mic()
    {
        try { return _devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch { return null; } // no microphone
    }

    public IReadOnlyList<AudioDevice> Devices(DataFlow flow)
    {
        try
        {
            string? def = null;
            try { def = _devices.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID; } catch { }
            return _devices.EnumerateAudioEndPoints(flow, DeviceState.Active)
                .Select(d => new AudioDevice(d.ID, d.FriendlyName, d.ID == def))
                .OrderByDescending(d => d.IsDefault).ToList();
        }
        catch { return []; }
    }

    /// <summary>Makes a device the default for every role (like picking it in Quick Settings).</summary>
    public static void SetDefaultDevice(string id)
    {
        var config = (IPolicyConfig)new CPolicyConfigClient();
        try
        {
            for (int role = 0; role < 3; role++) config.SetDefaultEndpoint(id, role); // console, multimedia, communications
        }
        finally { Marshal.ReleaseComObject(config); }
    }

    // ponytail: Windows has no public API to toggle Night light (its state is an
    // undocumented CloudStore blob). Open its page instead of risking a corrupt write.
    public static void OpenNightLight() => OpenSettings("ms-settings:nightlight");

    public static void OpenSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    public void Dispose() => _devices.Dispose();

    // Undocumented but stable since Windows 7; used by EarTrumpet, SoundSwitch and others.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class CPolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod(string id, bool def, IntPtr defPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }
}
