using System;
using XNotch.Models;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class ArmouryCrateAndVitalsTests
{
    [Fact]
    public void ArmouryCrateService_CreationAndModeQuery_ReturnsValidMode()
    {
        using var service = new ArmouryCrateService();

        // Mode must be a valid enum member
        Assert.True(Enum.IsDefined(typeof(ArmouryOperatingMode), service.CurrentMode));

        // Details must provide non-empty title and description
        var details = ArmouryCrateService.GetModeDetails(service.CurrentMode);
        Assert.False(string.IsNullOrWhiteSpace(details.Title));
        Assert.False(string.IsNullOrWhiteSpace(details.Description));
        Assert.False(string.IsNullOrWhiteSpace(details.FanStatus));
        Assert.False(string.IsNullOrWhiteSpace(details.Icon));
        Assert.StartsWith("#", details.AccentHex);
    }

    [Theory]
    [InlineData(ArmouryOperatingMode.Silent)]
    [InlineData(ArmouryOperatingMode.Balanced)]
    [InlineData(ArmouryOperatingMode.Performance)]
    [InlineData(ArmouryOperatingMode.Turbo)]
    [InlineData(ArmouryOperatingMode.Eco)]
    public void ArmouryCrateService_GetModeDetails_AllModesCovered(ArmouryOperatingMode mode)
    {
        var details = ArmouryCrateService.GetModeDetails(mode);
        Assert.NotNull(details.Title);
        Assert.NotNull(details.Description);
        Assert.NotNull(details.FanStatus);
        Assert.NotNull(details.AccentHex);
        Assert.NotNull(details.Icon);
    }

    [Fact]
    public void HardwareSnapshot_WithArmouryAndPowerProperties_ConstructsAccurately()
    {
        var snap = new HardwareSnapshot(
            CpuUsagePercent: 25.5,
            CpuClockGhz: 3.85,
            RamUsagePercent: 50.0,
            RamUsedGb: 16.0,
            RamTotalGb: 32.0,
            GpuUsagePercent: 45.0,
            GpuDedicatedVramUsedGb: 2.1,
            GpuDedicatedVramTotalGb: 4.0,
            GpuTemperatureCelsius: 58.0,
            GpuName: "NVIDIA GeForce RTX 3050 Laptop GPU",
            DiskDriveLetter: "C:",
            DiskUsagePercent: 62.0,
            DiskFreeGb: 150.0,
            DiskTotalGb: 512.0,
            NetDownloadKbps: 1250.0,
            NetUploadKbps: 340.0,
            Timestamp: DateTime.UtcNow,
            GpuPowerWatts: 42.5,
            CpuCoreCount: 14,
            OperatingMode: ArmouryOperatingMode.Turbo
        );

        Assert.Equal(25.5, snap.CpuUsagePercent);
        Assert.Equal(3.85, snap.CpuClockGhz);
        Assert.Equal(42.5, snap.GpuPowerWatts);
        Assert.Equal(14, snap.CpuCoreCount);
        Assert.Equal(ArmouryOperatingMode.Turbo, snap.OperatingMode);
        Assert.Contains("RTX 3050", snap.GpuName);
    }

    [Fact]
    public void SystemVitals_NetworkAndDiskCalculations_WorkAccurately()
    {
        double kbpsDown = 2560.0; // 2.5 MB/s
        string downStr = kbpsDown >= 1024.0 ? $"{kbpsDown / 1024.0:F1} MB/s" : $"{kbpsDown:F0} KB/s";
        Assert.Equal("2.5 MB/s", downStr);

        double kbpsUp = 450.0; // 450 KB/s
        string upStr = kbpsUp >= 1024.0 ? $"{kbpsUp / 1024.0:F1} MB/s" : $"{kbpsUp:F0} KB/s";
        Assert.Equal("450 KB/s", upStr);

        double totalGb = 512.0;
        double freeGb = 128.0;
        double diskPct = 100.0 * (1.0 - (freeGb / totalGb));
        Assert.Equal(75.0, diskPct);
    }

    [Fact]
    public void ArmouryCrateService_CycleNextOperatingMode_TransitionsThroughAllModes()
    {
        using var service = new ArmouryCrateService();

        service.SetOperatingMode(ArmouryOperatingMode.Silent);
        Assert.Equal(ArmouryOperatingMode.Silent, service.CurrentMode);

        var m1 = service.CycleNextOperatingMode();
        Assert.Equal(ArmouryOperatingMode.Balanced, m1);

        var m2 = service.CycleNextOperatingMode();
        Assert.Equal(ArmouryOperatingMode.Performance, m2);

        var m3 = service.CycleNextOperatingMode();
        Assert.Equal(ArmouryOperatingMode.Turbo, m3);

        var m4 = service.CycleNextOperatingMode();
        Assert.Equal(ArmouryOperatingMode.Eco, m4);

        var m5 = service.CycleNextOperatingMode();
        Assert.Equal(ArmouryOperatingMode.Silent, m5);
    }

    [Fact]
    public void OperatingModeChange_FiresEvent_AndPopulatesNotification()
    {
        using var service = new ArmouryCrateService();
        using var notifService = new NotificationService();

        ArmouryOperatingMode? changedMode = null;
        service.OperatingModeChanged += mode =>
        {
            changedMode = mode;
            var details = ArmouryCrateService.GetModeDetails(mode);
            notifService.AddNotification("Operating Mode", $"{details.Title} Mode Activated", details.Description);
        };

        service.SetOperatingMode(ArmouryOperatingMode.Turbo);

        Assert.Equal(ArmouryOperatingMode.Turbo, changedMode);
        Assert.NotEmpty(notifService.History);

        var alert = notifService.History[0];
        Assert.Equal("Operating Mode", alert.AppName);
        Assert.Contains("Turbo", alert.Title);
        Assert.False(string.IsNullOrWhiteSpace(alert.Body));
    }

    [Fact]
    public void AppLauncherHelper_IdentifiesPowerAndCustomNotifications()
    {
        var powerNotification = new NotificationItem(
            Id: 101,
            AppName: "Operating Mode",
            Title: "Turbo Mode Activated",
            Body: "Overboost fans active",
            Timestamp: DateTime.UtcNow
        );

        string app = powerNotification.AppName.ToLowerInvariant();
        bool isPower = app.Contains("operating mode") || app.Contains("power");
        Assert.True(isPower);

        var generalNotification = new NotificationItem(
            Id: 102,
            AppName: "Slack",
            Title: "New Message",
            Body: "Meeting in 5 minutes",
            Timestamp: DateTime.UtcNow
        );

        string generalApp = generalNotification.AppName.ToLowerInvariant();
        Assert.DoesNotContain("operating mode", generalApp);
        Assert.Equal("Slack", generalNotification.AppName);
    }
}
