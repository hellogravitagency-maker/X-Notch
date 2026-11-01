using System;

namespace XNotch.Models;

public sealed record HardwareSnapshot(
    double CpuUsagePercent,
    double CpuClockGhz,
    double RamUsagePercent,
    double RamUsedGb,
    double RamTotalGb,
    double GpuUsagePercent,
    double GpuDedicatedVramUsedGb,
    double GpuDedicatedVramTotalGb,
    double GpuTemperatureCelsius,
    string GpuName,
    string DiskDriveLetter,
    double DiskUsagePercent,
    double DiskFreeGb,
    double DiskTotalGb,
    double NetDownloadKbps,
    double NetUploadKbps,
    DateTime Timestamp,
    double GpuPowerWatts = 0,
    int CpuCoreCount = 14,
    Services.ArmouryOperatingMode OperatingMode = Services.ArmouryOperatingMode.Performance
);
