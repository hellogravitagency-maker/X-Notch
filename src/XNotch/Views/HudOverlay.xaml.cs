using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using XNotch.Services;

namespace XNotch.Views;

public partial class HudOverlay : UserControl
{
    private VolumeService? _volumeService;
    private BrightnessService? _brightnessService;
    private DispatcherTimer? _dismissTimer;
    private HudType _currentType;
    private float _lastFraction;

    /// <summary>When false, Show() is a no-op (prevents dual-instance conflicts).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Reflects the ShowVolumeHud setting.</summary>
    public bool VolumeHudEnabled { get; set; } = true;

    /// <summary>Reflects the ShowBrightnessHud setting.</summary>
    public bool BrightnessHudEnabled { get; set; } = true;

    private enum HudType { None, Volume, Brightness }

    public event Action? HudShown;
    public event Action? HudDismissed;

    public HudOverlay()
    {
        InitializeComponent();

        // The fill is proportional to the track width, which is 0 while the HUD
        // is collapsed — re-apply once layout produces a real width.
        if (SliderFill.Parent is Border track)
            track.SizeChanged += (_, _) => UpdateSliderFill(_lastFraction);
    }

    public void Bind(VolumeService volumeService, BrightnessService brightnessService)
    {
        _volumeService = volumeService;
        _brightnessService = brightnessService;

        // Callbacks arrive on non-UI threads (CoreAudio/WMI) — never block them.
        volumeService.VolumeChanged += (volume, muted) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (VolumeHudEnabled)
                    ShowVolume(volume, muted);
            });
        };

        if (brightnessService.IsSupported)
        {
            brightnessService.BrightnessChanged += brightness =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (BrightnessHudEnabled)
                        ShowBrightness(brightness);
                });
            };
        }
    }

    private void ShowVolume(float volume, bool muted)
    {
        _currentType = HudType.Volume;

        string icon = muted ? "🔇" : volume switch
        {
            < 0.01f => "🔇",
            < 0.33f => "🔈",
            < 0.66f => "🔉",
            _ => "🔊"
        };

        HudIcon.Text = icon;
        int percent = (int)(volume * 100);
        HudPercent.Text = muted ? "Mute" : $"{percent}%";
        UpdateSliderFill(muted ? 0 : volume);

        if (muted || volume < 0.01f)
        {
            HudIconBorder.Background = new SolidColorBrush(Color.FromArgb(45, 0xFF, 0x45, 0x3A));
            HudIconBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0xFF, 0x45, 0x3A));
            HudIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
            SliderFill.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        }
        else
        {
            HudIconBorder.Background = new SolidColorBrush(Color.FromArgb(28, 0xFF, 0xFF, 0xFF));
            HudIconBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0xFF, 0xFF, 0xFF));
            HudIcon.Foreground = Brushes.White;
            SliderFill.Background = Brushes.White;
        }

        Show();
    }

    private void ShowBrightness(int brightness)
    {
        _currentType = HudType.Brightness;

        HudIcon.Text = brightness switch
        {
            < 33 => "🔅",
            _ => "🔆"
        };

        HudPercent.Text = $"{brightness}%";
        UpdateSliderFill(brightness / 100f);

        // Apple solar gold accent for brightness
        var amber = Color.FromRgb(0xFF, 0xD6, 0x0A);
        HudIconBorder.Background = new SolidColorBrush(Color.FromArgb(45, amber.R, amber.G, amber.B));
        HudIconBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, amber.R, amber.G, amber.B));
        HudIcon.Foreground = new SolidColorBrush(amber);
        SliderFill.Background = new SolidColorBrush(amber);

        Show();
    }

    private void UpdateSliderFill(float fraction)
    {
        _lastFraction = fraction;
        if (SliderFill.Parent is Border parent && parent.ActualWidth > 0)
        {
            SliderFill.Width = Math.Max(0, fraction * parent.ActualWidth);
        }
    }

    private void Show()
    {
        if (!IsActive) return;

        Visibility = Visibility.Visible;
        HudShown?.Invoke();

        // Apple Dynamic Island fluid zoom-and-bloom
        var scaleAnim = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150));
        HudScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        HudScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        // Auto-dismiss after 2 seconds
        if (_dismissTimer == null)
        {
            _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _dismissTimer.Tick += (_, _) =>
            {
                _dismissTimer!.Stop();
                Dismiss();
            };
        }
        _dismissTimer.Stop();
        _dismissTimer.Start();
    }

    private void Dismiss()
    {
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            _currentType = HudType.None;
            HudDismissed?.Invoke();
        };
        BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void OnSliderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border bar || bar.ActualWidth <= 0) return;
        var pos = e.GetPosition(bar);
        float fraction = (float)Math.Clamp(pos.X / bar.ActualWidth, 0, 1);

        switch (_currentType)
        {
            case HudType.Volume:
                _volumeService?.SetVolume(fraction);
                break;
            case HudType.Brightness:
                _brightnessService?.SetBrightness((int)(fraction * 100));
                break;
        }

        // Reset dismiss timer
        Show();
    }
}
