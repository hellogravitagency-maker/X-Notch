using System;
using System.Windows.Forms;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XNotch.Services;

/// <summary>
/// Monitors battery status (charge level, charging state, estimated time remaining).
/// Uses System.Windows.Forms.SystemInformation.PowerStatus for reliable battery info.
/// </summary>
public partial class BatteryService : ObservableObject, IDisposable
{
    private DispatcherTimer? _pollTimer;

    [ObservableProperty] private int _chargePercent;
    [ObservableProperty] private bool _isCharging;
    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private TimeSpan _timeRemaining;
    [ObservableProperty] private bool _isFullyCharged;

    /// <summary>Fired when charging state changes (plugged in / unplugged)</summary>
    public event Action<bool>? ChargingStateChanged;

    public void Initialize()
    {
        UpdateBatteryInfo();

        // Poll every 10 seconds
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _pollTimer.Tick += (_, _) => UpdateBatteryInfo();
        _pollTimer.Start();
    }

    private void UpdateBatteryInfo()
    {
        var power = SystemInformation.PowerStatus;

        bool hadBattery = HasBattery;
        bool wasCharging = IsCharging;

        HasBattery = power.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery;

        if (!HasBattery)
        {
            ChargePercent = 0;
            IsCharging = false;
            TimeRemaining = TimeSpan.Zero;
            IsFullyCharged = false;
            if (_pollTimer != null && _pollTimer.Interval.TotalSeconds < 60)
                _pollTimer.Interval = TimeSpan.FromSeconds(60);
            return;
        }

        if (_pollTimer != null && _pollTimer.Interval.TotalSeconds > 10)
            _pollTimer.Interval = TimeSpan.FromSeconds(10);

        ChargePercent = (int)(power.BatteryLifePercent * 100);
        IsCharging = power.PowerLineStatus == PowerLineStatus.Online;
        IsFullyCharged = IsCharging && ChargePercent >= 100;

        int secondsRemaining = power.BatteryLifeRemaining;
        TimeRemaining = secondsRemaining > 0
            ? TimeSpan.FromSeconds(secondsRemaining)
            : TimeSpan.Zero;

        // Fire event on charging state transition
        if (wasCharging != IsCharging && hadBattery)
        {
            ChargingStateChanged?.Invoke(IsCharging);
        }
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
    }
}
