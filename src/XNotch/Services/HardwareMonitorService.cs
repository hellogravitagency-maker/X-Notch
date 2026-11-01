using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// High-efficiency system hardware telemetry service measuring CPU, RAM,
/// GPU, dedicated VRAM, GPU temperature, power consumption (Watts), fixed disk partitions,
/// and network throughput. Seamlessly integrates with NVML for NVIDIA RTX/GTX GPUs
/// and ASUS Armoury Crate operating modes.
/// Uses a consumer-gated background sampler to achieve 0% idle CPU overhead.
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    #region Win32 P/Invoke & Structs

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint DateTimeLow;
        public uint DateTimeHigh;

        public ulong ToUInt64() => ((ulong)DateTimeHigh << 32) | DateTimeLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public static MEMORYSTATUSEX Create() => new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // CPU Frequency via CallNtPowerInformation
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll")]
    private static extern int CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        [Out] PROCESSOR_POWER_INFORMATION[] outputBuffer,
        uint outputBufferLength
    );

    // D3DKMT Types for Fallback GPU Temperature
    private const uint KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERPERFDATA
    {
        public uint PhysicalAdapterIndex;
        public uint MaxMemoryBandwidth;
        public uint MaxMemoryBandwidthWin;
        public uint MaxMemoryBandwidthGpu;
        public uint Temperature; // in deci-Celsius (e.g. 520 = 52.0°C)
        public uint Power;
        public uint FanRpm;
        public uint FanPwm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public uint Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_OPENADAPTERFROMHDC
    {
        public IntPtr hDc;
        public uint hAdapter;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO pData);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromHdc(ref D3DKMT_OPENADAPTERFROMHDC pData);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER pData);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    #endregion

    #region NVML (NVIDIA Management Library) P/Invoke

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    private static extern int NvmlShutdown();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int NvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
    private static extern int NvmlDeviceGetPowerUsage(IntPtr device, out uint powerMw);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization rates);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
    private static extern int NvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
    private static extern int NvmlDeviceGetName(IntPtr device, [Out] byte[] name, uint length);

    #endregion

    private readonly object _lock = new();
    private readonly AutoResetEvent _wakeEvent = new(false);
    private int _consumerCount;
    private bool _disposed;
    private Thread? _samplerThread;

    // CPU Tracking State
    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private double _smoothedCpu;
    private readonly int _coreCount = Environment.ProcessorCount;

    // Network Tracking State
    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTime _prevNetTime = DateTime.UtcNow;

    // Disk Drive Cycling
    private readonly List<DriveInfo> _fixedDrives = [];
    private int _currentDriveIndex;

    // Fallback GPU Adapter State (D3DKMT)
    private uint _gpuAdapterHandle;

    // NVML GPU State
    private bool _nvmlInitialized;
    private IntPtr _nvmlDeviceHandle = IntPtr.Zero;
    private string _gpuName = "GPU";
    private double _gpuVramTotalGb = 4.0;

    // Armoury Crate Service
    private ArmouryCrateService? _armouryService;

    public HardwareSnapshot? CurrentSnapshot { get; private set; }

    public event Action<HardwareSnapshot>? SnapshotUpdated;

    public HardwareMonitorService(ArmouryCrateService? armouryService = null)
    {
        _armouryService = armouryService;
        InitFixedDrives();
        InitNvml();
        if (!_nvmlInitialized)
        {
            InitFallbackGpuAdapter();
        }

        _samplerThread = new Thread(SamplingLoop)
        {
            Name = "XNotch.HardwareSampler",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _samplerThread.Start();
    }

    public void SetArmouryCrateService(ArmouryCrateService service)
    {
        _armouryService = service;
    }

    private void InitNvml()
    {
        try
        {
            string nvmlPath = Path.Combine(Environment.SystemDirectory, "nvml.dll");
            if (!File.Exists(nvmlPath)) return;

            if (NvmlInit() == 0)
            {
                if (NvmlDeviceGetHandleByIndex(0, out _nvmlDeviceHandle) == 0 && _nvmlDeviceHandle != IntPtr.Zero)
                {
                    _nvmlInitialized = true;

                    byte[] nameBuf = new byte[64];
                    if (NvmlDeviceGetName(_nvmlDeviceHandle, nameBuf, (uint)nameBuf.Length) == 0)
                    {
                        string n = System.Text.Encoding.ASCII.GetString(nameBuf).TrimEnd('\0', ' ', '\r', '\n');
                        if (!string.IsNullOrWhiteSpace(n)) _gpuName = n;
                    }

                    if (NvmlDeviceGetMemoryInfo(_nvmlDeviceHandle, out var mem) == 0 && mem.Total > 0)
                    {
                        _gpuVramTotalGb = mem.Total / (1024.0 * 1024.0 * 1024.0);
                    }
                }
            }
        }
        catch
        {
            _nvmlInitialized = false;
        }
    }

    private void InitFixedDrives()
    {
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady)
                {
                    _fixedDrives.Add(d);
                }
            }
        }
        catch { }
    }

    private void InitFallbackGpuAdapter()
    {
        try
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                var openData = new D3DKMT_OPENADAPTERFROMHDC { hDc = hdc };
                if (D3DKMTOpenAdapterFromHdc(ref openData) == 0)
                {
                    _gpuAdapterHandle = openData.hAdapter;
                }
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        catch { }
    }

    public void RegisterConsumer()
    {
        if (Interlocked.Increment(ref _consumerCount) == 1)
        {
            _wakeEvent.Set();
        }
    }

    public void UnregisterConsumer()
    {
        Interlocked.Decrement(ref _consumerCount);
    }

    public void CycleNextDisk()
    {
        if (_fixedDrives.Count == 0) return;
        lock (_lock)
        {
            _currentDriveIndex = (_currentDriveIndex + 1) % _fixedDrives.Count;
        }
        _wakeEvent.Set();
    }

    private void SamplingLoop()
    {
        while (!_disposed)
        {
            try
            {
                // Consumer gate: wait until at least 1 UI view is consuming hardware data
                while (_consumerCount <= 0 && !_disposed)
                {
                    _wakeEvent.WaitOne(TimeSpan.FromSeconds(5));
                }

                if (_disposed) break;

                var snapshot = SampleSystemMetrics();
                CurrentSnapshot = snapshot;
                SnapshotUpdated?.Invoke(snapshot);

                if (_disposed) break;

                // Sample cadence: 1 Hz (1000ms)
                _wakeEvent.WaitOne(1000);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HardwareSampler error: {ex.Message}");
            }
        }
    }

    private HardwareSnapshot SampleSystemMetrics()
    {
        // 1. CPU Usage % via GetSystemTimes delta
        double cpuPercent = _smoothedCpu;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong idleVal = idle.ToUInt64();
            ulong kernelVal = kernel.ToUInt64();
            ulong userVal = user.ToUInt64();

            if (_prevKernelTime > 0 && _prevUserTime > 0)
            {
                ulong kernelDelta = kernelVal - _prevKernelTime;
                ulong userDelta = userVal - _prevUserTime;
                ulong idleDelta = idleVal - _prevIdleTime;
                ulong totalSys = kernelDelta + userDelta;

                if (totalSys > 0)
                {
                    double instantCpu = 100.0 * (1.0 - ((double)idleDelta / totalSys));
                    instantCpu = Math.Clamp(instantCpu, 0.0, 100.0);
                    _smoothedCpu = _smoothedCpu * 0.3 + instantCpu * 0.7;
                    cpuPercent = _smoothedCpu;
                }
            }

            _prevIdleTime = idleVal;
            _prevKernelTime = kernelVal;
            _prevUserTime = userVal;
        }

        // 2. Real-time CPU Clock GHz via CallNtPowerInformation
        double cpuGhz = 2.3;
        try
        {
            int procCount = Environment.ProcessorCount;
            var powerInfo = new PROCESSOR_POWER_INFORMATION[procCount];
            uint bufSize = (uint)(Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>() * procCount);
            if (CallNtPowerInformation(11, IntPtr.Zero, 0, powerInfo, bufSize) == 0 && powerInfo.Length > 0)
            {
                uint mhz = powerInfo[0].CurrentMhz;
                if (mhz > 0)
                {
                    cpuGhz = mhz / 1000.0;
                }
            }
        }
        catch { }

        // 3. RAM via GlobalMemoryStatusEx
        double ramPercent = 0;
        double ramUsedGb = 0;
        double ramTotalGb = 16.0;
        var memStatus = MEMORYSTATUSEX.Create();
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            ramPercent = memStatus.dwMemoryLoad;
            ramTotalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            ramUsedGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
        }

        // 4. GPU Metrics (NVML primary, D3DKMT fallback)
        double gpuPercent = 0;
        double gpuVramUsedGb = 0;
        double gpuVramTotalGb = _gpuVramTotalGb;
        double gpuTemp = 0;
        double gpuPowerWatts = 0;

        if (_nvmlInitialized && _nvmlDeviceHandle != IntPtr.Zero)
        {
            try
            {
                if (NvmlDeviceGetUtilizationRates(_nvmlDeviceHandle, out var util) == 0)
                {
                    gpuPercent = util.Gpu;
                }

                if (NvmlDeviceGetTemperature(_nvmlDeviceHandle, 0, out uint tempC) == 0)
                {
                    gpuTemp = tempC;
                }

                if (NvmlDeviceGetPowerUsage(_nvmlDeviceHandle, out uint powerMw) == 0)
                {
                    gpuPowerWatts = powerMw / 1000.0;
                }

                if (NvmlDeviceGetMemoryInfo(_nvmlDeviceHandle, out var mem) == 0)
                {
                    gpuVramTotalGb = mem.Total / (1024.0 * 1024.0 * 1024.0);
                    gpuVramUsedGb = mem.Used / (1024.0 * 1024.0 * 1024.0);
                }
            }
            catch { }
        }
        else if (_gpuAdapterHandle != 0)
        {
            try
            {
                var perfData = new D3DKMT_ADAPTERPERFDATA();
                int size = Marshal.SizeOf<D3DKMT_ADAPTERPERFDATA>();
                IntPtr pMem = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(perfData, pMem, false);

                var query = new D3DKMT_QUERYADAPTERINFO
                {
                    hAdapter = _gpuAdapterHandle,
                    Type = KMTQAITYPE_ADAPTERPERFDATA,
                    pPrivateDriverData = pMem,
                    PrivateDriverDataSize = (uint)size
                };

                if (D3DKMTQueryAdapterInfo(ref query) == 0)
                {
                    perfData = Marshal.PtrToStructure<D3DKMT_ADAPTERPERFDATA>(pMem);
                    if (perfData.Temperature > 0)
                    {
                        gpuTemp = perfData.Temperature / 10.0; // deci-Celsius
                    }
                }
                Marshal.FreeHGlobal(pMem);
            }
            catch { }
        }

        // 5. Disk Drive Metrics
        string diskLetter = "C:";
        double diskUsagePercent = 0;
        double diskFreeGb = 0;
        double diskTotalGb = 0;
        if (_fixedDrives.Count > 0)
        {
            lock (_lock)
            {
                if (_currentDriveIndex >= _fixedDrives.Count) _currentDriveIndex = 0;
                var drive = _fixedDrives[_currentDriveIndex];
                try
                {
                    diskLetter = drive.Name.TrimEnd('\\');
                    diskTotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    diskFreeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    if (diskTotalGb > 0)
                    {
                        diskUsagePercent = 100.0 * (1.0 - (diskFreeGb / diskTotalGb));
                    }
                }
                catch { }
            }
        }

        // 6. Network Throughput
        double downKbps = 0;
        double upKbps = 0;
        try
        {
            long currentRecv = 0;
            long currentSent = 0;
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = ni.GetIPv4Statistics();
                    currentRecv += stats.BytesReceived;
                    currentSent += stats.BytesSent;
                }
            }

            var now = DateTime.UtcNow;
            double elapsedSec = (now - _prevNetTime).TotalSeconds;
            if (elapsedSec > 0 && _prevBytesReceived > 0)
            {
                downKbps = Math.Max(0, (currentRecv - _prevBytesReceived) / (1024.0 * elapsedSec));
                upKbps = Math.Max(0, (currentSent - _prevBytesSent) / (1024.0 * elapsedSec));
            }

            _prevBytesReceived = currentRecv;
            _prevBytesSent = currentSent;
            _prevNetTime = now;
        }
        catch { }

        // Operating mode sync
        ArmouryOperatingMode mode = ArmouryOperatingMode.Performance;
        if (_armouryService != null)
        {
            _armouryService.RefreshActiveMode();
            mode = _armouryService.CurrentMode;
        }

        return new HardwareSnapshot(
            CpuUsagePercent: Math.Round(cpuPercent, 1),
            CpuClockGhz: Math.Round(cpuGhz, 2),
            RamUsagePercent: Math.Round(ramPercent, 1),
            RamUsedGb: Math.Round(ramUsedGb, 1),
            RamTotalGb: Math.Round(ramTotalGb, 1),
            GpuUsagePercent: Math.Round(gpuPercent, 1),
            GpuDedicatedVramUsedGb: Math.Round(gpuVramUsedGb, 1),
            GpuDedicatedVramTotalGb: Math.Round(gpuVramTotalGb, 1),
            GpuTemperatureCelsius: Math.Round(gpuTemp, 1),
            GpuName: _gpuName,
            DiskDriveLetter: diskLetter,
            DiskUsagePercent: Math.Round(diskUsagePercent, 1),
            DiskFreeGb: Math.Round(diskFreeGb, 1),
            DiskTotalGb: Math.Round(diskTotalGb, 1),
            NetDownloadKbps: Math.Round(downKbps, 1),
            NetUploadKbps: Math.Round(upKbps, 1),
            Timestamp: DateTime.UtcNow,
            GpuPowerWatts: Math.Round(gpuPowerWatts, 1),
            CpuCoreCount: _coreCount,
            OperatingMode: mode
        );
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _wakeEvent.Set(); } catch { }

        if (_samplerThread?.IsAlive == true)
        {
            _samplerThread.Join(200);
        }

        if (_nvmlInitialized)
        {
            try { NvmlShutdown(); } catch { }
            _nvmlInitialized = false;
            _nvmlDeviceHandle = IntPtr.Zero;
        }

        if (_gpuAdapterHandle != 0)
        {
            try
            {
                var closeData = new D3DKMT_CLOSEADAPTER { hAdapter = _gpuAdapterHandle };
                D3DKMTCloseAdapter(ref closeData);
            }
            catch { }
            _gpuAdapterHandle = 0;
        }

        try { _wakeEvent.Dispose(); } catch { }
    }
}
