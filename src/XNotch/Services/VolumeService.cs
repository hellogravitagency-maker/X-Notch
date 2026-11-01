using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace XNotch.Services;

/// <summary>
/// Monitors and controls system volume using NAudio's WASAPI endpoint.
/// Fires VolumeChanged when the user changes volume (via keyboard, etc.),
/// and automatically switches endpoints when headphones/Bluetooth headsets connect.
/// </summary>
public class VolumeService : IDisposable, IMMNotificationClient
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;
    private readonly object _lock = new();

    public float Volume
    {
        get
        {
            lock (_lock)
            {
                return _endpointVolume?.MasterVolumeLevelScalar ?? 0f;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_lock)
            {
                return _endpointVolume?.Mute ?? false;
            }
        }
    }

    public event Action<float, bool>? VolumeChanged;

    public void Initialize()
    {
        lock (_lock)
        {
            try
            {
                _enumerator = new MMDeviceEnumerator();
                _enumerator.RegisterEndpointNotificationCallback(this);
                BindDefaultDevice();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VolumeService init failed: {ex.Message}");
            }
        }
    }

    private void BindDefaultDevice()
    {
        if (_enumerator == null) return;
        try
        {
            if (_endpointVolume != null)
                _endpointVolume.OnVolumeNotification -= OnVolumeNotification;
            _device?.Dispose();

            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpointVolume = _device.AudioEndpointVolume;
            _endpointVolume.OnVolumeNotification += OnVolumeNotification;

            VolumeChanged?.Invoke(Volume, IsMuted);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VolumeService bind device failed: {ex.Message}");
        }
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
    }

    public void SetVolume(float level)
    {
        lock (_lock)
        {
            if (_endpointVolume == null) return;
            _endpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
        }
    }

    public void ToggleMute()
    {
        lock (_lock)
        {
            if (_endpointVolume == null) return;
            _endpointVolume.Mute = !_endpointVolume.Mute;
        }
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && (role == Role.Multimedia || role == Role.Console))
        {
            lock (_lock)
            {
                BindDefaultDevice();
            }
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_enumerator != null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            }
            if (_endpointVolume != null)
                _endpointVolume.OnVolumeNotification -= OnVolumeNotification;
            _device?.Dispose();
            _enumerator?.Dispose();
            _endpointVolume = null;
            _device = null;
            _enumerator = null;
        }
    }
}
