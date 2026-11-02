using System;
using System.Windows;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;

namespace XNotch.Views;

public partial class InlineSettingsView : System.Windows.Controls.UserControl
{
    private AppSettings? _settings;
    private bool _isLoading;
    private readonly UpdateService _updateService = new();

    /// <summary>Fired when any setting changes.</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>Fired when the user clicks the back arrow.</summary>
    public event Action? BackRequested;

    /// <summary>Fired when theme-related appearance changes.</summary>
    public event Action<AppSettings>? ThemeChangeRequested;

    /// <summary>Fired when the user wants to manage plugins.</summary>
    public event Action? ManagePluginsRequested;

    public InlineSettingsView()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.NameWithVersion;
    }

    /// <summary>Resets the settings scroll viewer back to the top.</summary>
    public void ResetScroll()
    {
        SettingsScrollViewer?.ScrollToTop();
    }

    private void OnSettingsPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (SettingsScrollViewer != null)
        {
            if (SettingsScrollViewer.ScrollableHeight > 0)
            {
                double step = e.Delta / 2.0;
                double targetOffset = SettingsScrollViewer.VerticalOffset - step;
                SettingsScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, SettingsScrollViewer.ScrollableHeight));
            }
            e.Handled = true;
        }
    }

    public void LoadSettings(AppSettings settings)
    {
        _isLoading = true;
        _settings = settings;

        StartOnBootCheck.IsChecked = settings.StartOnBoot;
        AutoHideCheck.IsChecked = settings.AutoHide;
        CursorCenterPlacementCheck.IsChecked = settings.CursorCenterPlacement;
        CameraHoverPopUpCheck.IsChecked = settings.CameraHoverPopUp;
        ShowShadowCheck.IsChecked = settings.ShowShadow;
        ShowMusicCheck.IsChecked = settings.ShowMusicControls;
        ShowVisualizerCheck.IsChecked = settings.ShowVisualizer;
        VolumeHudCheck.IsChecked = settings.ShowVolumeHud;
        BrightnessHudCheck.IsChecked = settings.ShowBrightnessHud;
        ShowMusicCardCheck.IsChecked = settings.ShowMusicCard;
        ShowStopwatchCardCheck.IsChecked = settings.ShowStopwatchCard;
        ShowWaterCardCheck.IsChecked = settings.ShowWaterCard;
        ShowSleepCardCheck.IsChecked = settings.ShowSleepCard;
        ShowTasksCardCheck.IsChecked = settings.ShowTasksCard;
        ShowVitalsCardCheck.IsChecked = settings.ShowVitalsCard;
        ShowNotificationsCardCheck.IsChecked = settings.ShowNotificationsCard;
        ShowBatteryCheck.IsChecked = settings.ShowBattery;
        ShowCalendarCheck.IsChecked = settings.ShowCalendar;
        ShowWebcamCheck.IsChecked = settings.ShowWebcam;
        WebcamFpsSlider.Value = settings.WebcamFps;
        WebcamFpsLabel.Text = $"{settings.WebcamFps}";
        AdaptToWindowsThemeCheck.IsChecked = settings.AdaptToWindowsTheme;
        ShowOnAllScreensCheck.IsChecked = settings.ShowOnAllScreens;
        SyncIslandsCheck.IsChecked = settings.SyncIslandsOpenState;
        ReserveSpaceCheck.IsChecked = settings.ReserveSpaceAtTop;

        if (settings.HoverMode == HoverMode.LongHoverOpen)
            LongHoverRadio.IsChecked = true;
        else
            HoverPeekRadio.IsChecked = true;

        if (settings.Theme == AppTheme.LiquidGlass || settings.UseLiquidGlassTheme)
            ThemeGlassRadio.IsChecked = true;
        else if (settings.GetManualTheme() == AppTheme.Light)
            ThemeLightRadio.IsChecked = true;
        else
            ThemeDarkRadio.IsChecked = true;

        RefreshWorkspaceRoots();

        _isLoading = false;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.StartOnBoot = StartOnBootCheck.IsChecked == true;
        _settings.AutoHide = AutoHideCheck.IsChecked == true;
        _settings.CursorCenterPlacement = CursorCenterPlacementCheck.IsChecked == true;
        _settings.CameraHoverPopUp = CameraHoverPopUpCheck.IsChecked == true;
        _settings.ShowShadow = ShowShadowCheck.IsChecked == true;
        _settings.ShowMusicControls = ShowMusicCheck.IsChecked == true;
        _settings.ShowVisualizer = ShowVisualizerCheck.IsChecked == true;
        _settings.ShowVolumeHud = VolumeHudCheck.IsChecked == true;
        _settings.ShowBrightnessHud = BrightnessHudCheck.IsChecked == true;
        _settings.ShowMusicCard = ShowMusicCardCheck.IsChecked == true;
        _settings.ShowStopwatchCard = ShowStopwatchCardCheck.IsChecked == true;
        _settings.ShowWaterCard = ShowWaterCardCheck.IsChecked == true;
        _settings.ShowSleepCard = ShowSleepCardCheck.IsChecked == true;
        _settings.ShowTasksCard = ShowTasksCardCheck.IsChecked == true;
        _settings.ShowVitalsCard = ShowVitalsCardCheck.IsChecked == true;
        _settings.ShowNotificationsCard = ShowNotificationsCardCheck.IsChecked == true;
        _settings.ShowBattery = ShowBatteryCheck.IsChecked == true;
        _settings.ShowCalendar = ShowCalendarCheck.IsChecked == true;
        _settings.ShowWebcam = ShowWebcamCheck.IsChecked == true;
        _settings.ShowOnAllScreens = ShowOnAllScreensCheck.IsChecked == true;
        _settings.SyncIslandsOpenState = SyncIslandsCheck.IsChecked == true;
        _settings.ReserveSpaceAtTop = ReserveSpaceCheck.IsChecked == true;

        _settings.ApplyStartOnBoot();
        _settings.Save();
        SettingsChanged?.Invoke(_settings);
    }

    private void OnHoverModeChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.HoverMode = LongHoverRadio.IsChecked == true
            ? HoverMode.LongHoverOpen
            : HoverMode.HoverPeekClickOpen;

        _settings.Save();
        SettingsChanged?.Invoke(_settings);
    }

    private void OnWebcamFpsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading || _settings == null) return;
        _settings.WebcamFps = (int)e.NewValue;
        WebcamFpsLabel.Text = $"{_settings.WebcamFps}";
        _settings.Save();
        SettingsChanged?.Invoke(_settings);
    }

    public void UpdateWebcamFpsMax(double maxFps)
    {
        if (maxFps > 5)
            WebcamFpsSlider.Maximum = Math.Min(maxFps, 60);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.AdaptToWindowsTheme = false;
        AdaptToWindowsThemeCheck.IsChecked = false;

        if (ThemeGlassRadio.IsChecked == true)
        {
            _settings.Theme = AppTheme.LiquidGlass;
            _settings.UseLiquidGlassTheme = true;
        }
        else if (ThemeLightRadio.IsChecked == true)
        {
            _settings.Theme = AppTheme.Light;
            _settings.UseLiquidGlassTheme = false;
        }
        else
        {
            _settings.Theme = AppTheme.Dark;
            _settings.UseLiquidGlassTheme = false;
        }

        _settings.Save();
        ThemeChangeRequested?.Invoke(_settings);
    }

    private void OnAdaptThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.AdaptToWindowsTheme = AdaptToWindowsThemeCheck.IsChecked == true;
        _settings.Save();
        ThemeChangeRequested?.Invoke(_settings);
    }



    private void OnManagePluginsClick(object sender, RoutedEventArgs e)
    {
        ManagePluginsRequested?.Invoke();
    }

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateText.Text = "Checking...";
        CheckUpdateButton.IsEnabled = false;

        var result = await _updateService.CheckForUpdateAsync();

        switch (result)
        {
            case UpdateCheckResult.UpdateAvailable:
                CheckUpdateButton.Visibility = Visibility.Collapsed;
                UpdateButton.Visibility = Visibility.Visible;
                UpdateButtonText.Text = $"⬇ Update to v{_updateService.LatestVersion}";
                break;
            case UpdateCheckResult.CheckFailed:
                CheckUpdateText.Text = "Couldn't check — see log";
                break;
            default:
                CheckUpdateText.Text = "You're up to date ✓";
                break;
        }

        CheckUpdateButton.IsEnabled = true;
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateButtonText.Text = "Downloading...";
        UpdateButton.IsEnabled = false;

        var progress = new Progress<double>(p =>
        {
            var pct = (int)(p * 100);
            UpdateButtonText.Text = $"Downloading... {pct}%";
        });

        var (ok, error) = await _updateService.DownloadAndLaunchInstallerAsync(progress);
        if (!ok)
        {
            UpdateButtonText.Text = error ?? "Update failed — retry";
            UpdateButton.IsEnabled = true;
        }
    }

    private void RefreshWorkspaceRoots()
    {
        if (_settings == null) return;
        WorkspaceRootsList.ItemsSource = null;
        WorkspaceRootsList.ItemsSource = System.Linq.Enumerable.ToList(_settings.WorkspaceRoots);
    }

    private void OnAddWorkspaceRootClick(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Workspace Folder to Scan for Projects"
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            string path = dlg.FolderName.Trim();
            if (!System.Linq.Enumerable.Any(_settings.WorkspaceRoots, r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.WorkspaceRoots.Add(path);
                _settings.Save(immediate: true);
                RefreshWorkspaceRoots();
                SettingsChanged?.Invoke(_settings);
            }
        }
    }

    private void OnRemoveWorkspaceRootClick(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (sender is FrameworkElement fe && fe.Tag is string rootPath)
        {
            var match = System.Linq.Enumerable.FirstOrDefault(_settings.WorkspaceRoots, r => string.Equals(r, rootPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _settings.WorkspaceRoots.Remove(match);
                _settings.Save(immediate: true);
                RefreshWorkspaceRoots();
                SettingsChanged?.Invoke(_settings);
            }
        }
    }
}
