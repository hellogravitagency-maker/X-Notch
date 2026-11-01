using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Models;
using XNotch.Services;

namespace XNotch.Views;

public partial class SystemVitalsWidgetView : UserControl, IDisposable
{
    private HardwareMonitorService? _hwService;
    private BatteryService? _batteryService;
    private ArmouryCrateService? _armouryService;
    private bool _disposed;

    // macOS monochrome segmented capsule brushes
    private static readonly SolidColorBrush ActiveSegmentBrush = new(Color.FromRgb(0x2C, 0x2C, 0x30));
    private static readonly SolidColorBrush InactiveSegmentBrush = Brushes.Transparent;
    private static readonly SolidColorBrush ActiveTextBrush = Brushes.White;
    private static readonly SolidColorBrush InactiveTextBrush = new(Color.FromRgb(0x8E, 0x8E, 0x93));

    public SystemVitalsWidgetView()
    {
        InitializeComponent();
    }

    public void Bind(HardwareMonitorService hwService, BatteryService batteryService, ArmouryCrateService? armouryService = null)
    {
        _hwService = hwService;
        _batteryService = batteryService;
        _armouryService = armouryService;

        _hwService.RegisterConsumer();
        _hwService.SnapshotUpdated += OnSnapshotUpdated;

        if (_armouryService != null)
        {
            _armouryService.OperatingModeChanged += OnOperatingModeChanged;
            UpdateOperatingModeVisuals(_armouryService.CurrentMode);
        }

        _batteryService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(BatteryService.ChargePercent) or nameof(BatteryService.IsCharging))
            {
                Dispatcher.Invoke(UpdateBatteryVisuals);
            }
        };

        try
        {
            string host = Environment.MachineName;
            DeviceModelBadge.Text = host.Length > 14 ? host[..14] : host;
            DeviceModelBadge.ToolTip = $"{host} ({Environment.OSVersion.VersionString})";
        }
        catch
        {
            DeviceModelBadge.Text = "VITALS";
        }

        if (_hwService.CurrentSnapshot != null)
        {
            OnSnapshotUpdated(_hwService.CurrentSnapshot);
        }

        UpdateBatteryVisuals();
    }

    private void OnSnapshotUpdated(HardwareSnapshot snap)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // 1. CPU Telemetry (Page 1)
            int cpuPct = (int)Math.Round(snap.CpuUsagePercent);
            CpuText.Text = $"{cpuPct}%";
            CpuSubtitle.Text = snap.CpuClockGhz > 0 ? $"{snap.CpuClockGhz:F2} GHz" : $"{snap.CpuCoreCount}C";
            CpuCoresPill.Text = $"{snap.CpuCoreCount} Cores";
            CpuDetailText.Text = snap.CpuClockGhz > 0 ? $"{snap.CpuClockGhz:F2} GHz Dynamic" : "Active";
            SetBarPercent(CpuFillCol, CpuEmptyCol, cpuPct);

            // 2. Memory (RAM) Telemetry (Page 1)
            int ramPct = (int)Math.Round(snap.RamUsagePercent);
            RamText.Text = $"{ramPct}%";
            RamSubtitle.Text = $"{snap.RamUsedGb:F1} GB";
            RamTotalText.Text = $"/ {snap.RamTotalGb:F0} GB";
            double freeGb = Math.Max(0, snap.RamTotalGb - snap.RamUsedGb);
            RamDetailText.Text = $"{freeGb:F1} GB Available";
            SetBarPercent(RamFillCol, RamEmptyCol, ramPct);

            // 3. Graphics (GPU) Telemetry (Page 1)
            int gpuPct = (int)Math.Round(snap.GpuUsagePercent);
            GpuText.Text = $"{gpuPct}%";
            GpuSubtitle.Text = snap.GpuTemperatureCelsius > 0 ? $"{snap.GpuTemperatureCelsius:F0}°C" : "Active";

            if (!string.IsNullOrWhiteSpace(snap.GpuName) && snap.GpuName != "GPU")
            {
                string shortName = snap.GpuName;
                if (shortName.StartsWith("NVIDIA GeForce ", StringComparison.OrdinalIgnoreCase))
                    shortName = shortName["NVIDIA GeForce ".Length..];
                else if (shortName.StartsWith("AMD Radeon ", StringComparison.OrdinalIgnoreCase))
                    shortName = shortName["AMD Radeon ".Length..];
                else if (shortName.StartsWith("Intel(R) ", StringComparison.OrdinalIgnoreCase))
                    shortName = shortName["Intel(R) ".Length..];

                GpuModePill.Text = shortName.Length > 9 ? shortName[..9].Trim() : shortName;
                GpuModePill.ToolTip = snap.GpuName;
            }
            else
            {
                GpuModePill.Text = "GPU";
            }

            if (snap.GpuDedicatedVramTotalGb > 0)
            {
                GpuDetailText.Text = $"{snap.GpuDedicatedVramUsedGb:F1} / {snap.GpuDedicatedVramTotalGb:F0} GB";
            }
            else
            {
                GpuDetailText.Text = "Unified Memory";
            }
            SetBarPercent(GpuFillCol, GpuEmptyCol, gpuPct);

            // 4. Storage (Page 2)
            int diskPct = (int)Math.Round(snap.DiskUsagePercent);
            DiskText.Text = $"{diskPct}%";
            DiskLetterBadge.Text = string.IsNullOrEmpty(snap.DiskDriveLetter) ? "C: ⟳" : $"{snap.DiskDriveLetter} ⟳";
            DiskFreeText.Text = $"{snap.DiskFreeGb:F0} GB Free";
            DiskDetailText.Text = $"{snap.DiskTotalGb:F0} GB Total";
            SetBarPercent(DiskFillCol, DiskEmptyCol, snap.DiskUsagePercent);

            // 5. Network (Page 2)
            NetDownText.Text = FormatNetworkSpeed(snap.NetDownloadKbps);
            NetUpText.Text = FormatNetworkSpeed(snap.NetUploadKbps);

            UpdateBatteryVisuals();
        });
    }

    private static void SetBarPercent(ColumnDefinition fillCol, ColumnDefinition emptyCol, double pct)
    {
        double clamped = Math.Clamp(pct, 0.0, 100.0);
        fillCol.Width = new GridLength(clamped, GridUnitType.Star);
        emptyCol.Width = new GridLength(Math.Max(0.01, 100.0 - clamped), GridUnitType.Star);
    }

    private static string FormatNetworkSpeed(double kbps)
    {
        if (kbps >= 1024.0 * 1024.0)
        {
            return $"{kbps / (1024.0 * 1024.0):F1} GB/s";
        }
        if (kbps >= 1024.0)
        {
            return $"{kbps / 1024.0:F1} MB/s";
        }
        return $"{kbps:F0} KB/s";
    }

    private void UpdateBatteryVisuals()
    {
        if (_batteryService == null || !_batteryService.HasBattery)
        {
            PowerIcon.Text = "⚡";
            PowerStatusText.Text = "AC Power Connected";
        }
        else
        {
            int pct = _batteryService.ChargePercent;
            bool charging = _batteryService.IsCharging;
            PowerIcon.Text = charging ? "⚡" : "🔋";
            PowerStatusText.Text = charging ? $"Charging ({pct}%)" : $"Battery ({pct}%)";
        }
    }

    private void OnTabVitalsClick(object sender, RoutedEventArgs e)
    {
        TabVitalsButton.Background = ActiveSegmentBrush;
        TabVitalsText.Foreground = ActiveTextBrush;
        TabDetailsButton.Background = InactiveSegmentBrush;
        TabDetailsText.Foreground = InactiveTextBrush;

        PageVitalsGrid.Visibility = Visibility.Visible;
        PageDetailsGrid.Visibility = Visibility.Collapsed;
    }

    private void OnTabDetailsClick(object sender, RoutedEventArgs e)
    {
        TabDetailsButton.Background = ActiveSegmentBrush;
        TabDetailsText.Foreground = ActiveTextBrush;
        TabVitalsButton.Background = InactiveSegmentBrush;
        TabVitalsText.Foreground = InactiveTextBrush;

        PageVitalsGrid.Visibility = Visibility.Collapsed;
        PageDetailsGrid.Visibility = Visibility.Visible;
    }

    private void OnOperatingModeChanged(ArmouryOperatingMode mode)
    {
        Dispatcher.Invoke(() => UpdateOperatingModeVisuals(mode));
    }

    private void UpdateOperatingModeVisuals(ArmouryOperatingMode mode)
    {
        var details = ArmouryCrateService.GetModeDetails(mode);
        OperatingModeText.Text = details.Title;
        OperatingModeIcon.Text = details.Icon;
        OperatingModeButton.ToolTip = $"Power Mode: {details.Title} ({details.Description}). Click to cycle.";
    }

    private void OnOperatingModeClick(object sender, RoutedEventArgs e)
    {
        _armouryService?.CycleNextOperatingMode();
    }

    private void OnDiskCycleClick(object sender, RoutedEventArgs e)
    {
        _hwService?.CycleNextDisk();
    }

    private void OnTaskManagerClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch
        {
            // Ignore if security policy restricts execution
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwService != null)
        {
            _hwService.SnapshotUpdated -= OnSnapshotUpdated;
            _hwService.UnregisterConsumer();
        }

        if (_armouryService != null)
        {
            _armouryService.OperatingModeChanged -= OnOperatingModeChanged;
        }
    }
}
