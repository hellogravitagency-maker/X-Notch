using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace XNotch.Services;

/// <summary>
/// Monitors USB and removable storage drive connection and disconnection events via WM_DEVICECHANGE.
/// Surfaces Apple Dynamic Island drive mount and eject alerts.
/// </summary>
public sealed class UsbDeviceService
{
    public const int WM_DEVICECHANGE = 0x0219;
    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 0x00000002;

    private readonly ConcurrentDictionary<string, long> _lastEventTimes = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, string, long>? UsbDriveConnected;
    public event Action<string>? UsbDriveDisconnected;

    public void HandleDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        int eventType = wParam.ToInt32();
        if (eventType != DBT_DEVICEARRIVAL && eventType != DBT_DEVICEREMOVECOMPLETE)
            return;

        if (lParam == IntPtr.Zero) return;

        try
        {
            var header = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
            if (header.dbch_devicetype != DBT_DEVTYP_VOLUME)
                return;

            var volume = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);
            string driveLetter = GetDriveLetterFromMask(volume.dbcv_unitmask);
            if (string.IsNullOrWhiteSpace(driveLetter)) return;

            // Debounce rapid repeated events for the same drive/event within 1.5 seconds
            long now = Environment.TickCount64;
            string eventKey = $"{eventType}:{driveLetter}";
            if (_lastEventTimes.TryGetValue(eventKey, out long last) && now - last < 1500)
                return;
            _lastEventTimes[eventKey] = now;

            if (eventType == DBT_DEVICEARRIVAL)
            {
                string label = "USB Drive";
                long totalBytes = 0;
                try
                {
                    var drive = new DriveInfo(driveLetter + ":\\");
                    if (drive.IsReady)
                    {
                        if (!string.IsNullOrWhiteSpace(drive.VolumeLabel))
                            label = drive.VolumeLabel;
                        totalBytes = drive.TotalSize;
                    }
                }
                catch { }

                UsbDriveConnected?.Invoke(driveLetter, label, totalBytes);
            }
            else if (eventType == DBT_DEVICEREMOVECOMPLETE)
            {
                UsbDriveDisconnected?.Invoke(driveLetter);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UsbDeviceService] Error: {ex.Message}");
        }
    }

    private static string GetDriveLetterFromMask(uint mask)
    {
        for (int i = 0; i < 26; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                return ((char)('A' + i)).ToString();
            }
        }
        return string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR
    {
        public int dbch_size;
        public int dbch_devicetype;
        public int dbch_reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_VOLUME
    {
        public int dbcv_size;
        public int dbcv_devicetype;
        public int dbcv_reserved;
        public uint dbcv_unitmask;
        public ushort dbcv_flags;
    }
}
