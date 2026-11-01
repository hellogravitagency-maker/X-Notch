using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Services;

namespace XNotch.Views;

public partial class BatteryIndicator : UserControl
{
    private BatteryService? _batteryService;
    private bool _userEnabled = true;
    private bool _suppressed;

    public BatteryIndicator()
    {
        InitializeComponent();
    }

    /// <summary>Reflects the ShowBattery setting; battery updates never override it.</summary>
    public bool UserEnabled
    {
        get => _userEnabled;
        set { _userEnabled = value; UpdateUI(); }
    }

    /// <summary>Temporarily hidden (e.g. while the HUD occupies the closed notch).</summary>
    public bool Suppressed
    {
        get => _suppressed;
        set { _suppressed = value; UpdateUI(); }
    }

    public void Bind(BatteryService service)
    {
        _batteryService = service;

        service.PropertyChanged += OnServicePropertyChanged;
        UpdateUI();
    }

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(UpdateUI);
    }

    private void UpdateUI()
    {
        if (_batteryService == null) return;

        if (!_userEnabled || _suppressed || !_batteryService.HasBattery)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        int percent = _batteryService.ChargePercent;
        bool charging = _batteryService.IsCharging;

        PercentText.Text = $"{percent}%";
        BoltIcon.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;

        // Battery fill width: inner width is ~14.5px
        double maxFillWidth = 14.5;
        BatteryFill.Width = Math.Max(0, (percent / 100.0) * maxFillWidth);

        // Assign the shared theme brushes directly — ThemeService mutates them
        // in place, so colors stay live across theme switches.
        if (charging)
            BatteryFill.Background = GetThemeBrush("SuccessBrush", Color.FromRgb(0x30, 0xD1, 0x58));
        else if (percent <= 10)
            BatteryFill.Background = GetThemeBrush("DangerBrush", Color.FromRgb(0xFF, 0x45, 0x3A));
        else if (percent <= 20)
            BatteryFill.Background = GetThemeBrush("WarningBrush", Color.FromRgb(0xFF, 0xD6, 0x0A));
        else
            BatteryFill.Background = GetThemeBrush("BatteryNormalFillBrush", Colors.White);

        if (percent <= 10)
            PercentText.Foreground = GetThemeBrush("DangerBrush", Color.FromRgb(0xFF, 0x45, 0x3A));
        else if (percent <= 20)
            PercentText.Foreground = GetThemeBrush("WarningBrush", Color.FromRgb(0xFF, 0xD6, 0x0A));
        else
            PercentText.Foreground = GetThemeBrush("BatteryTextBrush", Color.FromRgb(0x99, 0x99, 0x99));
    }

    private System.Windows.Media.Brush GetThemeBrush(string resourceKey, Color fallback)
    {
        return TryFindResource(resourceKey) as System.Windows.Media.Brush
            ?? new SolidColorBrush(fallback);
    }
}
