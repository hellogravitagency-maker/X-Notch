using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using XNotch.Models;

namespace XNotch.Views;

public partial class WaterTrackerWidgetView : UserControl
{
    private AppSettings? _settings;

    public WaterTrackerWidgetView()
    {
        InitializeComponent();
    }

    public void Bind(AppSettings settings)
    {
        _settings = settings;

        // Reset if new day
        string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
        if (_settings.WaterLastLogDate != todayStr)
        {
            _settings.WaterLastLogDate = todayStr;
            _settings.WaterCurrentMl = 0;
            _settings.Save();
        }

        UpdateVisuals(animate: false);
    }

    private void OnAdd250Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.WaterCurrentMl = Math.Min(10000, _settings.WaterCurrentMl + 250);
        SaveAndUpdate();
    }

    private void OnAdd500Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.WaterCurrentMl = Math.Min(10000, _settings.WaterCurrentMl + 500);
        SaveAndUpdate();
    }

    private void OnSub250Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.WaterCurrentMl = Math.Max(0, _settings.WaterCurrentMl - 250);
        SaveAndUpdate();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.WaterCurrentMl = 0;
        SaveAndUpdate();
    }

    private void SaveAndUpdate()
    {
        if (_settings == null) return;
        _settings.WaterLastLogDate = DateTime.Now.ToString("yyyy-MM-dd");
        _settings.Save();
        UpdateVisuals(animate: true);
    }

    private void UpdateVisuals(bool animate = true)
    {
        if (_settings == null) return;

        int current = _settings.WaterCurrentMl;
        int goal = _settings.WaterGoalMl > 0 ? _settings.WaterGoalMl : 2000;
        double ratio = Math.Clamp((double)current / goal, 0.0, 1.0);
        int percent = (int)(ratio * 100);

        CurrentAmountText.Text = $"{current:N0}";
        GoalAmountText.Text = $" / {goal:N0} ml";
        PercentText.Text = $"{percent}%";

        // Flask total height is 96
        double targetHeight = Math.Max(6, 96 * ratio);
        double targetBarWidth = Math.Max(0, 180 * ratio);

        if (animate)
        {
            var anim = new DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            LiquidFill.BeginAnimation(FrameworkElement.HeightProperty, anim);

            var barAnim = new DoubleAnimation(targetBarWidth, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressBarFill.BeginAnimation(FrameworkElement.WidthProperty, barAnim);
        }
        else
        {
            LiquidFill.Height = targetHeight;
            ProgressBarFill.Width = targetBarWidth;
        }

        int remainingMl = Math.Max(0, goal - current);
        if (remainingMl == 0)
        {
            StatusMessageText.Text = "🎉 Daily hydration goal achieved!";
            StatusMessageText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xF5, 0xD4));
        }
        else
        {
            int glasses = (int)Math.Ceiling(remainingMl / 250.0);
            StatusMessageText.Text = $"{remainingMl:N0} ml ({glasses} glass{(glasses > 1 ? "es" : "")}) remaining today";
            StatusMessageText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        }
    }
}
