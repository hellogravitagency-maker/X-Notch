using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace XNotch.Views;

public partial class StopwatchWidgetView : UserControl, IDisposable
{
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _timer;
    private readonly List<TimeSpan> _laps = [];
    private TimeSpan _lastLapMark = TimeSpan.Zero;
    private bool _disposed;

    public StopwatchWidgetView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += OnTimerTick;
    }

    private void OnLapsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (LapsScrollViewer != null)
        {
            double targetOffset = LapsScrollViewer.VerticalOffset - (e.Delta / 2.5);
            LapsScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, LapsScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_stopwatch.IsRunning)
        {
            // Pause
            _stopwatch.Stop();
            _timer.Stop();
            StartStopButton.Background = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)); // Green
            StartStopText.Text = "Resume";
            StartStopText.Foreground = Brushes.Black;
            LapResetText.Text = "Reset";
            RunningDot.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Start
            _stopwatch.Start();
            _timer.Start();
            StartStopButton.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)); // Coral Red
            StartStopText.Text = "Stop";
            StartStopText.Foreground = Brushes.White;
            LapResetText.Text = "Lap";
            RunningDot.Visibility = Visibility.Visible;
        }
    }

    private void OnLapResetClick(object sender, RoutedEventArgs e)
    {
        if (_stopwatch.IsRunning)
        {
            // Record Lap
            var current = _stopwatch.Elapsed;
            var lapSplit = current - _lastLapMark;
            _lastLapMark = current;
            _laps.Add(lapSplit);
            RenderLaps();
        }
        else
        {
            // Reset
            _stopwatch.Reset();
            _lastLapMark = TimeSpan.Zero;
            _laps.Clear();
            TimeMinutesSeconds.Text = "00:00";
            TimeHundredths.Text = ".00";
            StartStopText.Text = "Start";
            StartStopButton.Background = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            StartStopText.Foreground = Brushes.Black;
            LapResetText.Text = "Reset";
            RunningDot.Visibility = Visibility.Collapsed;
            RenderLaps();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var ts = _stopwatch.Elapsed;
        int minutes = (int)ts.TotalMinutes;
        int seconds = ts.Seconds;
        int hundredths = ts.Milliseconds / 10;
        TimeMinutesSeconds.Text = $"{minutes:D2}:{seconds:D2}";
        TimeHundredths.Text = $".{hundredths:D2}";
    }

    private static string FormatStopwatch(TimeSpan ts)
    {
        int minutes = (int)ts.TotalMinutes;
        int seconds = ts.Seconds;
        int hundredths = ts.Milliseconds / 10;
        return $"{minutes:D2}:{seconds:D2}.{hundredths:D2}";
    }

    private void RenderLaps()
    {
        LapsPanel.Children.Clear();

        if (_laps.Count == 0)
        {
            var emptyBorder = new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 12, 6, 12),
                Margin = new Thickness(0, 8, 0, 0)
            };
            emptyBorder.Child = new TextBlock
            {
                Text = "No recorded laps",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("TextFaintestBrush")
            };
            LapsPanel.Children.Add(emptyBorder);
            return;
        }

        // Find min and max laps if >= 2 laps
        int fastestIdx = -1;
        int slowestIdx = -1;
        if (_laps.Count >= 2)
        {
            TimeSpan min = TimeSpan.MaxValue;
            TimeSpan max = TimeSpan.MinValue;
            for (int i = 0; i < _laps.Count; i++)
            {
                if (_laps[i] < min) { min = _laps[i]; fastestIdx = i; }
                if (_laps[i] > max) { max = _laps[i]; slowestIdx = i; }
            }
        }

        // Render from newest lap down to oldest
        for (int i = _laps.Count - 1; i >= 0; i--)
        {
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 1.5, 0, 1.5)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Brush textBrush = (Brush)FindResource("TextPrimaryBrush");
            if (i == fastestIdx) textBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)); // Emerald Green
            else if (i == slowestIdx) textBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)); // Coral Red

            var lapTitlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            lapTitlePanel.Children.Add(new TextBlock
            {
                Text = $"Lap {i + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

            if (i == fastestIdx)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x28, 0x30, 0xD1, 0x58)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 0.5, 3, 0.5),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Text = "Fastest",
                    FontSize = 7.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
                };
                lapTitlePanel.Children.Add(badge);
            }

            Grid.SetColumn(lapTitlePanel, 0);

            var lapTime = new TextBlock
            {
                Text = FormatStopwatch(_laps[i]),
                FontSize = 10.5,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
                FontWeight = FontWeights.Bold,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lapTime, 1);

            grid.Children.Add(lapTitlePanel);
            grid.Children.Add(lapTime);
            rowBorder.Child = grid;

            LapsPanel.Children.Add(rowBorder);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopwatch.Stop();
        _timer.Stop();
    }
}
