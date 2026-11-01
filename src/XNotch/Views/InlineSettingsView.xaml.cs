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
        else if (settings.Theme == AppTheme.White)
            ThemeWhiteRadio.IsChecked = true;
        else if (settings.Theme == AppTheme.Custom)
            ThemeCustomRadio.IsChecked = true;
        else if (settings.GetManualTheme() == AppTheme.Light)
            ThemeLightRadio.IsChecked = true;
        else
            ThemeDarkRadio.IsChecked = true;

        AccentHexInput.Text = settings.CustomAccentColor;
        BgHexInput.Text = settings.CustomBackgroundColor;
        AccentPreviewBox.Background = new System.Windows.Media.SolidColorBrush(ColorHelper.ParseColor(settings.CustomAccentColor, System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xFF)));
        BgPreviewBox.Background = new System.Windows.Media.SolidColorBrush(ColorHelper.ParseColor(settings.CustomBackgroundColor, System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A)));
        CustomThemeSettingsPanel.Visibility = settings.Theme == AppTheme.Custom ? Visibility.Visible : Visibility.Collapsed;

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
        else if (ThemeWhiteRadio.IsChecked == true)
        {
            _settings.Theme = AppTheme.White;
            _settings.UseLiquidGlassTheme = false;
        }
        else if (ThemeCustomRadio.IsChecked == true)
        {
            _settings.Theme = AppTheme.Custom;
            _settings.UseLiquidGlassTheme = false;
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

        CustomThemeSettingsPanel.Visibility = _settings.Theme == AppTheme.Custom ? Visibility.Visible : Visibility.Collapsed;

        _settings.Save();
        ThemeChangeRequested?.Invoke(_settings);
    }

    private void OnAccentColorPresetClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not string hex) return;
        _settings.CustomAccentColor = hex;
        _isLoading = true;
        AccentHexInput.Text = hex;
        AccentPreviewBox.Background = new System.Windows.Media.SolidColorBrush(ColorHelper.ParseColor(hex, System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xFF)));
        _isLoading = false;

        _settings.AdaptToWindowsTheme = false;
        AdaptToWindowsThemeCheck.IsChecked = false;
        _settings.Save();
        ThemeChangeRequested?.Invoke(_settings);
    }

    private void OnAccentHexChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isLoading || _settings == null) return;
        string hex = AccentHexInput.Text.Trim();
        if (hex.Length is >= 4 and <= 9)
        {
            var parsed = ColorHelper.ParseColor(hex, System.Windows.Media.Colors.Transparent);
            if (parsed != System.Windows.Media.Colors.Transparent)
            {
                AccentPreviewBox.Background = new System.Windows.Media.SolidColorBrush(parsed);
                _settings.CustomAccentColor = hex;
                _settings.AdaptToWindowsTheme = false;
                AdaptToWindowsThemeCheck.IsChecked = false;
                _settings.Save();
                ThemeChangeRequested?.Invoke(_settings);
            }
        }
    }

    private void OnBgColorPresetClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not string hex) return;
        _settings.CustomBackgroundColor = hex;
        _isLoading = true;
        BgHexInput.Text = hex;
        BgPreviewBox.Background = new System.Windows.Media.SolidColorBrush(ColorHelper.ParseColor(hex, System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A)));
        _settings.Theme = AppTheme.Custom;
        ThemeCustomRadio.IsChecked = true;
        CustomThemeSettingsPanel.Visibility = Visibility.Visible;
        _isLoading = false;

        _settings.AdaptToWindowsTheme = false;
        AdaptToWindowsThemeCheck.IsChecked = false;
        _settings.Save();
        ThemeChangeRequested?.Invoke(_settings);
    }

    private void OnBgHexChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isLoading || _settings == null) return;
        string hex = BgHexInput.Text.Trim();
        if (hex.Length is >= 4 and <= 9)
        {
            var parsed = ColorHelper.ParseColor(hex, System.Windows.Media.Colors.Transparent);
            if (parsed != System.Windows.Media.Colors.Transparent)
            {
                BgPreviewBox.Background = new System.Windows.Media.SolidColorBrush(parsed);
                _settings.CustomBackgroundColor = hex;
                _settings.Theme = AppTheme.Custom;
                ThemeCustomRadio.IsChecked = true;
                CustomThemeSettingsPanel.Visibility = Visibility.Visible;
                _settings.AdaptToWindowsTheme = false;
                AdaptToWindowsThemeCheck.IsChecked = false;
                _settings.Save();
                ThemeChangeRequested?.Invoke(_settings);
            }
        }
    }

    private void OnQuickPresetClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not string preset) return;

        string bg;
        string accent;
        switch (preset)
        {
            case "Cyberpunk":
                bg = "#180D2B";
                accent = "#06B6D4";
                break;
            case "PureSnow":
                bg = "#FFFFFF";
                accent = "#007AFF";
                break;
            case "Emerald":
                bg = "#0A1F14";
                accent = "#10B981";
                break;
            case "Velvet":
                bg = "#250914";
                accent = "#EC4899";
                break;
            case "Amber":
                bg = "#141416";
                accent = "#F59E0B";
                break;
            default:
                bg = "#0F172A";
                accent = "#007AFF";
                break;
        }

        _settings.CustomBackgroundColor = bg;
        _settings.CustomAccentColor = accent;
        _settings.Theme = AppTheme.Custom;
        _settings.AdaptToWindowsTheme = false;

        _isLoading = true;
        ThemeCustomRadio.IsChecked = true;
        CustomThemeSettingsPanel.Visibility = Visibility.Visible;
        AdaptToWindowsThemeCheck.IsChecked = false;
        AccentHexInput.Text = accent;
        BgHexInput.Text = bg;
        AccentPreviewBox.Background = new System.Windows.Media.SolidColorBrush(ColorHelper.ParseColor(accent, System.Windows.Media.Colors.Blue));
        BgPreviewBox.Background = new System.Windows.Media.SolidColorBrush(ColorHelper.ParseColor(bg, System.Windows.Media.Colors.Black));
        _isLoading = false;

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
            Title = "Select Workspace Folder to Scan for Projects & Agents"
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
