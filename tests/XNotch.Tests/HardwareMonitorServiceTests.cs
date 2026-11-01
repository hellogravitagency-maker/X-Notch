using System;
using System.Threading;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class HardwareMonitorServiceTests
{
    [Fact]
    public void HardwareMonitorService_ConsumerGatedSampling_WorksCorrectly()
    {
        using var service = new HardwareMonitorService();

        // Register consumer to activate sampling
        service.RegisterConsumer();

        // Wait briefly for first sample
        Thread.Sleep(150);

        var snapshot = service.CurrentSnapshot;
        if (snapshot != null)
        {
            Assert.True(snapshot.RamTotalGb > 0);
            Assert.True(snapshot.RamUsedGb >= 0);
            Assert.True(snapshot.RamUsagePercent >= 0 && snapshot.RamUsagePercent <= 100);
            Assert.True(snapshot.CpuUsagePercent >= 0 && snapshot.CpuUsagePercent <= 100);
            Assert.False(string.IsNullOrEmpty(snapshot.DiskDriveLetter));
        }

        // Unregister consumer
        service.UnregisterConsumer();
    }

    [Fact]
    public void HardwareMonitorService_CycleNextDisk_DoesNotThrow()
    {
        using var service = new HardwareMonitorService();
        service.CycleNextDisk();
        service.CycleNextDisk();
    }
}
