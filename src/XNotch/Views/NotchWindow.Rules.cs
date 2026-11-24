using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XNotch.Helpers;
using XNotch.Services;

namespace XNotch.Views;

/// <summary>
/// Per-app rules (Normal / Slim / Hide / Game) for this island. One entry point,
/// <see cref="ApplyAppRule"/>, runs on foreground changes, fullscreen changes and settings changes.
/// </summary>
public partial class NotchWindow
{
    private AppRule _appliedRule = AppRule.Normal;
    private GameStripWindow? _gameStrip;
    private readonly ScaleTransform _slimScale = new(1, 1);

    private void ApplyAppRule()
    {
        var fg = _services.Foreground;
        var here = ScreenHelper.GetMonitorHandle(this);
        bool fullscreenHere = _fullscreenService.IsFullscreen && _fullscreenService.FullscreenMonitor == here;
        bool fgHere = fg.Window != IntPtr.Zero && MonitorFromWindow(fg.Window, 2 /* NEAREST */) == here;

        var rule = AppRules.Resolve(_settings.AppRules, fgHere ? fg.ExeName : null, fullscreenHere, _settings.FullscreenRule);
        if (rule == _appliedRule) return;
        _appliedRule = rule;

        bool visible = rule is AppRule.Normal or AppRule.Slim;
        IsHitTestVisible = visible;
        FadeCanvas(visible ? 1 : 0);

        // Slim: the closed island squashes to a thin bar from the top edge (transform only).
        NotchCanvas.RenderTransformOrigin = new Point(0.5, 0);
        NotchCanvas.RenderTransform = _slimScale;
        AnimateScaleY(rule == AppRule.Slim ? 0.4 : 1);
        ClosedContent.Visibility = rule == AppRule.Slim ? Visibility.Hidden : Visibility.Visible;

        // Game strip lives on this island's screen; FPS capture runs only while it shows.
        if (rule == AppRule.Game)
        {
            if (_gameStrip == null)
            {
                _gameStrip = new GameStripWindow(_services, ScreenHelper.FindScreenByDevice(_screenDevice));
                Closed += (_, _) => _gameStrip?.Close();
            }
            _services.Fps.Start();
            _gameStrip.Show();
        }
        else if (_gameStrip != null)
        {
            _gameStrip.Hide();
            if (!AnyGameStripVisible()) _services.Fps.Stop();
        }
    }

    private static bool AnyGameStripVisible()
    {
        foreach (Window w in Application.Current.Windows)
            if (w is GameStripWindow { IsVisible: true }) return true;
        return false;
    }

    // Exit faster than enter: 120 ms out, 180 ms in, strong ease-out.
    private void FadeCanvas(double to)
    {
        if (!SystemParameters.ClientAreaAnimation) { NotchCanvas.BeginAnimation(OpacityProperty, null); NotchCanvas.Opacity = to; return; }
        NotchCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 180 : 120))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void AnimateScaleY(double to)
    {
        if (!SystemParameters.ClientAreaAnimation) { _slimScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); _slimScale.ScaleY = to; return; }
        _slimScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
}
