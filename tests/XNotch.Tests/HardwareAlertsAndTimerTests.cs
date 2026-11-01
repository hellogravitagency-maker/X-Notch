using System;
using XNotch.Helpers;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class HardwareAlertsAndTimerTests
{
    [Fact]
    public void CountdownTimerService_LifecycleAndStateTransitions_WorkAccurately()
    {
        using var timer = new CountdownTimerService();
        timer.Initialize();

        Assert.Equal(CountdownTimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.RemainingTime);

        // Start 25m focus timer
        var duration = TimeSpan.FromMinutes(25);
        timer.Start(duration, "Pomodoro");

        Assert.Equal(CountdownTimerState.Running, timer.State);
        Assert.Equal("Pomodoro", timer.Label);
        Assert.Equal(duration, timer.TotalDuration);
        Assert.True(timer.RemainingTime.TotalSeconds > 0);
        Assert.StartsWith("2", timer.FormattedTime);

        // Pause
        timer.Pause();
        Assert.Equal(CountdownTimerState.Paused, timer.State);

        // Resume
        timer.Resume();
        Assert.Equal(CountdownTimerState.Running, timer.State);

        // Reset
        timer.Reset();
        Assert.Equal(CountdownTimerState.Idle, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.RemainingTime);
        Assert.Equal(TimeSpan.Zero, timer.TotalDuration);
    }

    [Fact]
    public void PrivacyIndicatorService_InstantiatesAndQueriesCleanly()
    {
        using var privacy = new PrivacyIndicatorService();
        // Should not throw and should have valid state
        privacy.CheckPrivacyStatus();

        // Defaults are clean
        Assert.False(privacy.IsCameraActive);
    }

    [Fact]
    public void LockKeyHook_StartAndStop_DoesNotThrow()
    {
        using var hook = new LockKeyHook();
        // Starting and stopping the hook should be graceful
        hook.Start();
        hook.Stop();
    }

    [Fact]
    public void UsbDeviceService_HandlesEmptyAndInvalidEventsGracefully()
    {
        var usb = new UsbDeviceService();
        // Invalid or null lParam should be safely ignored
        usb.HandleDeviceChange((IntPtr)UsbDeviceService.DBT_DEVICEARRIVAL, IntPtr.Zero);
        usb.HandleDeviceChange((IntPtr)0x9999, IntPtr.Zero);
    }
}
