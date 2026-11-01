using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace XNotch.Views;

public partial class ClockWidget : UserControl
{
    private DispatcherTimer? _timer;

    public ClockWidget()
    {
        InitializeComponent();
        UpdateTime();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_timer != null) return; // Loaded can fire again on re-attach

        // 1s tick so the minute flips on time (a 10s tick can lag the clock)
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateTime();
        _timer.Start();
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("ddd d", CultureInfo.CurrentCulture);
    }
}
