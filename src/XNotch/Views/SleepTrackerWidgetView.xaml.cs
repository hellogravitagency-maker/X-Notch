using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Models;
using Brush = System.Windows.Media.Brush;

namespace XNotch.Views;

public partial class SleepTrackerWidgetView : UserControl
{
    private AppSettings? _settings;

    public SleepTrackerWidgetView()
    {
        InitializeComponent();
    }

    public void Bind(AppSettings settings)
    {
        _settings = settings;
        UpdateVisuals();
    }

    private void OnMinus15Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.SleepLastNightHours = Math.Max(0.0, Math.Round(_settings.SleepLastNightHours - 0.25, 2));
        _settings.Save();
        UpdateVisuals();
    }

    private void OnPlus15Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.SleepLastNightHours = Math.Min(24.0, Math.Round(_settings.SleepLastNightHours + 0.25, 2));
        _settings.Save();
        UpdateVisuals();
    }

    private void OnWindDownToggleClick(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.SleepWindDownEnabled = !_settings.SleepWindDownEnabled;
        _settings.Save();
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_settings == null) return;

        double hours = _settings.SleepLastNightHours;
        int h = (int)hours;
        int m = (int)Math.Round((hours - h) * 60);
        SleepDurationText.Text = $"{h}h {m}m";

        double target = _settings.SleepTargetHours > 0 ? _settings.SleepTargetHours : 8.0;
        int qualityPct = Math.Clamp((int)((hours / target) * 100), 10, 100);
        SleepQualityText.Text = $"{qualityPct}% Rest";

        BedtimeText.Text = FormatTimeString(_settings.SleepBedtime, "11:00 PM");
        WakeTimeText.Text = FormatTimeString(_settings.SleepWakeTime, "07:00 AM");

        GoalSubtitleText.Text = $"Target: {target:F1} hrs • Wind-down {(_settings.SleepWindDownEnabled ? "Enabled" : "Off")}";

        if (_settings.SleepWindDownEnabled)
        {
            WindDownToggle.Background = new LinearGradientBrush(
                Color.FromArgb(0x28, 0x4F, 0x46, 0xE5),
                Color.FromArgb(0x12, 0x37, 0x30, 0xA3),
                new Point(0, 0), new Point(0, 1));
            WindDownToggle.BorderBrush = new LinearGradientBrush(
                Color.FromArgb(0x60, 0x81, 0x8C, 0xF8),
                Color.FromArgb(0x25, 0x4F, 0x46, 0xE5),
                new Point(0, 0), new Point(0, 1));
            if (WindDownLabel != null) WindDownLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            if (WindDownBadge != null) WindDownBadge.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x81, 0x8C, 0xF8));
            if (WindDownStatusText != null)
            {
                WindDownStatusText.Text = "ON";
                WindDownStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xFE));
            }
        }
        else
        {
            WindDownToggle.Background = new LinearGradientBrush(
                Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF),
                new Point(0, 0), new Point(0, 1));
            WindDownToggle.BorderBrush = new LinearGradientBrush(
                Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
                new Point(0, 0), new Point(0, 1));
            if (WindDownLabel != null) WindDownLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            if (WindDownBadge != null) WindDownBadge.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            if (WindDownStatusText != null)
            {
                WindDownStatusText.Text = "OFF";
                WindDownStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            }
        }
    }

    private static string FormatTimeString(string raw, string fallback)
    {
        if (TimeSpan.TryParse(raw, out var ts))
        {
            var dt = DateTime.Today.Add(ts);
            return dt.ToString("hh:mm tt");
        }
        return fallback;
    }
}
