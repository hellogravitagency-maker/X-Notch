using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Plugins;
using XNotch.Services;
using System.Linq;
using System.Windows.Interop;
using XNotch.Views.Panels;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace XNotch.Views;

public partial class NotchWindow : Window
{
    private readonly NotchViewModel _vm;

    // Spring animators for each animated property
    private readonly SpringAnimator _widthSpring;
    private readonly SpringAnimator _heightSpring;
    private readonly SpringAnimator _topRadiusSpring;
    private readonly SpringAnimator _bottomRadiusSpring;
    private readonly SpringAnimator _shadowOpacitySpring;
    private readonly SpringAnimator _contentOpacitySpring;
    private readonly SpringAnimator _contentScaleSpring;
    private readonly SpringAnimator _auraOpacitySpring;

    // Apple Dynamic Island notification & approval spring physics animators
    private readonly SpringAnimator _notificationScaleSpring = new(0.34, 0.72);
    private readonly SpringAnimator _notificationTranslateSpring = new(0.32, 0.75);
    private readonly SpringAnimator _notificationOpacitySpring = new(0.24, 0.88);
    private readonly SpringAnimator _notificationIconScaleSpring = new(0.28, 0.65);

    private readonly SpringAnimator _approvalScaleSpring = new(0.34, 0.72);
    private readonly SpringAnimator _approvalTranslateSpring = new(0.32, 0.75);
    private readonly SpringAnimator _approvalOpacitySpring = new(0.24, 0.88);
    private readonly SpringAnimator _approvalIconScaleSpring = new(0.28, 0.65);

    // Dynamic Island hardware & peripheral alerts (Power Surge, Lock State, USB)
    private readonly SpringAnimator _powerSurgeScaleSpring = new(0.34, 0.72);
    private readonly SpringAnimator _powerSurgeTranslateSpring = new(0.32, 0.75);
    private readonly SpringAnimator _powerSurgeOpacitySpring = new(0.24, 0.88);

    private readonly SpringAnimator _lockStateScaleSpring = new(0.34, 0.72);
    private readonly SpringAnimator _lockStateTranslateSpring = new(0.32, 0.75);
    private readonly SpringAnimator _lockStateOpacitySpring = new(0.24, 0.88);

    private readonly SpringAnimator _usbDriveScaleSpring = new(0.34, 0.72);
    private readonly SpringAnimator _usbDriveTranslateSpring = new(0.32, 0.75);
    private readonly SpringAnimator _usbDriveOpacitySpring = new(0.24, 0.88);

    // Current animated values
    private double _currentWidth;
    private double _currentHeight;
    private double _currentTopRadius;
    private double _currentBottomRadius;
    private double _currentShadowOpacity;
    private double _currentContentOpacity;
    private double _currentContentScale;
    private double _currentAuraOpacity;

    private double _currentNotificationScale = 0.88;
    private double _currentNotificationTranslate = -3.0;
    private double _currentNotificationOpacity = 0.0;
    private double _currentNotificationIconScale = 1.0;

    private double _currentApprovalScale = 0.88;
    private double _currentApprovalTranslate = -3.0;
    private double _currentApprovalOpacity = 0.0;
    private double _currentApprovalIconScale = 1.0;

    private double _currentPowerSurgeScale = 0.88;
    private double _currentPowerSurgeTranslate = -3.0;
    private double _currentPowerSurgeOpacity = 0.0;
    private bool _powerSurgeActive;
    private DispatcherTimer? _powerSurgeDismissTimer;

    private double _currentLockStateScale = 0.88;
    private double _currentLockStateTranslate = -3.0;
    private double _currentLockStateOpacity = 0.0;
    private bool _lockStateActive;
    private DispatcherTimer? _lockStateDismissTimer;

    private double _currentUsbDriveScale = 0.88;
    private double _currentUsbDriveTranslate = -3.0;
    private double _currentUsbDriveOpacity = 0.0;
    private bool _usbDriveActive;
    private DispatcherTimer? _usbDriveDismissTimer;
    private string? _activeUsbDriveLetter;

    private readonly SpringAnimator _operatingModeScaleSpring = new(0.34, 0.72);
    private readonly SpringAnimator _operatingModeTranslateSpring = new(0.32, 0.75);
    private readonly SpringAnimator _operatingModeOpacitySpring = new(0.24, 0.88);
    private double _currentOperatingModeScale = 0.88;
    private double _currentOperatingModeTranslate = -3.0;
    private double _currentOperatingModeOpacity = 0.0;
    private bool _operatingModeActive;
    private DispatcherTimer? _operatingModeDismissTimer;


    // Fixed window dimensions (large enough for open state + shadow padding)
    private double _fixedWindowWidth;
    private double _fixedWindowHeight;

    // Hover debounce
    private DispatcherTimer? _hoverOpenTimer;
    private DispatcherTimer? _hoverCloseTimer;
    private bool _isMouseOverNotch;
    private bool _isSettingsExpanded;
    private bool _isContextMenuOpen;

    // Notifications, Approvals & Intelligence
    private NotificationItem? _activeNotification;
    private DispatcherTimer? _notificationDismissTimer;
    private ApprovalRequest? _activeApproval;
    private SpotlightLauncher? _spotlightLauncher;

    // Camera under-bezel hover pop-up
    private DispatcherTimer? _cameraProximityTimer;
    private DispatcherTimer? _cameraPopUpTimer;
    private bool _isCameraPopUpActive;
    private bool _hasExitedCameraZone = true;

    // Resting idle memory trimmer
    private DispatcherTimer? _idleTrimTimer;
    private bool _wasAnimating;

    // Drag & Dock
    private NotchDock _dock = NotchDock.Top;
    private bool _isDragging;
    private bool _isMouseDownOnNotch;
    private Point _dragStartScreen;
    private Point _dragLastScreen;
    private DateTime _dragLastTime;
    private double _dragVelocityX;
    private double _dragVelocityY;

    // Multi-screen: which monitor this island lives on (WinForms device name).
    // All window placement is done in PHYSICAL pixels (Per-Monitor V2) — DIPs
    // are only used inside the window, scaled by the screen's own DPI.
    private string? _screenDevice;
    private System.Drawing.Rectangle _screenPhysical;
    private double _screenScale = 1.0;

    // Opt-in reserved screen strip (shell AppBar, like the taskbar)
    private readonly AppBarHelper _appBar = new();

    // Island role & cross-island sync
    private readonly bool _isPrimary;
    private bool _applyingSyncedState;

    /// <summary>Raised when the user opens/closes this island (not for synced changes).</summary>
    public event Action<NotchState>? UserStateChanged;

    /// <summary>Raised after the user changed settings from this island's inline panel.</summary>
    public event Action<AppSettings>? SettingsUpdatedByUser;

    /// <summary>Device name of the screen currently hosting this island.</summary>
    public string? ScreenDevice => _screenDevice;

    // Liquid Glass backdrop
    private bool _isLiquidGlassActive;
    private ImageBrush? _backdropBrush;
    private System.Windows.Media.Imaging.WriteableBitmap? _backdropWb;
    private int _wbW, _wbH;
    private double _cachedDpiX = 1, _cachedDpiY = 1;
    private bool _dpiCached;

    // Services (shared across all islands; owned by App via NotchServices)
    private readonly NotchServices _services;
    private readonly MediaService _mediaService;
    private readonly AudioCaptureService _audioCaptureService;
    private readonly VolumeService _volumeService;
    private readonly BrightnessService _brightnessService;
    private readonly BatteryService _batteryService;
    private readonly CalendarService _calendarService;
    private readonly ThemeService _themeService;
    private readonly ShelfService _shelfService;
    private readonly FullscreenService _fullscreenService;
    private readonly WebcamService _webcamService;
    private readonly GlobalHotkey? _globalHotkey;

    // Settings
    private AppSettings _settings;

    // Plugin system
    private PluginManager? _pluginManager;
    private PluginLibraryService? _pluginLibraryService;
    private PluginContext? _pluginContext;
    private Window? _pluginManagerWindow;

    /// <summary>
    /// The primary island hosts the plugin system, the global hotkey, and persists
    /// its dock position. Secondary islands (one per extra screen) share every
    /// service but skip those responsibilities. <paramref name="screenDevice"/> is
    /// the WinForms device name of the screen to appear on (null = primary screen).
    /// </summary>
    public NotchWindow(AppSettings settings, ThemeService themeService, NotchServices services,
        bool isPrimary = true, string? screenDevice = null)
    {
        InitializeComponent();

        _settings = settings;
        _isPrimary = isPrimary;
        _screenDevice = screenDevice;
        _vm = new NotchViewModel();
        DataContext = _vm;

        // Theme
        _themeService = themeService;

        // Initialize springs with Apple Dynamic Island physics
        _widthSpring = new SpringAnimator(0.38, 0.78);
        _heightSpring = new SpringAnimator(0.36, 0.80);
        _topRadiusSpring = new SpringAnimator(0.38, 0.85);
        _bottomRadiusSpring = new SpringAnimator(0.38, 0.85);
        _shadowOpacitySpring = new SpringAnimator(0.30, 0.90);
        _contentOpacitySpring = new SpringAnimator(0.35, 0.90);
        _contentScaleSpring = new SpringAnimator(0.36, 0.78);
        _auraOpacitySpring = new SpringAnimator(0.40, 0.82);

        // Set initial values
        _currentWidth = NotchConstants.ClosedWidth;
        _currentHeight = NotchConstants.ClosedHeight;
        _currentTopRadius = NotchConstants.ClosedTopRadius;
        _currentBottomRadius = NotchConstants.ClosedBottomRadius;
        _currentShadowOpacity = 0.0;
        _currentContentOpacity = 0.0;
        _currentContentScale = 0.92;
        _currentAuraOpacity = 0.0;

        // Shared services
        _services = services;
        _mediaService = services.Media;
        _audioCaptureService = services.AudioCapture;
        _volumeService = services.Volume;
        _brightnessService = services.Brightness;
        _batteryService = services.Battery;
        _calendarService = services.Calendar;
        _shelfService = services.Shelf;
        _fullscreenService = services.Fullscreen;
        _webcamService = services.Webcam;
        _globalHotkey = isPrimary ? new GlobalHotkey() : null;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Deactivated += OnNotchDeactivated;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.MakeOverlayWindow(this);
        _appBar.Attach(this);

        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        // Register global hotkeys (Ctrl+Alt+N and Ctrl+Space) — primary island only
        if (_globalHotkey != null)
        {
            _globalHotkey.Register(this);
            _globalHotkey.HotkeyPressed += OnGlobalHotkeyPressed;
            _globalHotkey.SpotlightHotkeyPressed += () => Dispatcher.BeginInvoke(OpenSpotlight);
        }
    }

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_ACTIVATE = 1;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            // When open, allow click activation so interactive controls (e.g. task input) receive focus and keystrokes
            if (_vm.NotchState == NotchState.Open)
            {
                handled = true;
                return (IntPtr)MA_ACTIVATE;
            }
        }
        else if (msg == UsbDeviceService.WM_DEVICECHANGE)
        {
            if (_isPrimary)
            {
                _services.Usb.HandleDeviceChange(wParam, lParam);
            }
        }
        return IntPtr.Zero;
    }

    private void OnNotchDeactivated(object? sender, EventArgs e)
    {
        // When user clicks away to another application while notch is open, gracefully close and return to overlay
        if (_vm.NotchState == NotchState.Open && !_isMouseOverNotch && !_isContextMenuOpen)
        {
            TransitionToClose();
        }
    }

    /// <summary>
    /// Claims or releases the dedicated top strip for this island's screen.
    /// Only meaningful while top-docked; side docks and drags release it.
    /// </summary>
    private void UpdateSpaceReservation()
    {
        if (_settings.ReserveSpaceAtTop && _dock == NotchDock.Top && !_isDragging)
        {
            var screen = ScreenHelper.FindScreenByDevice(_screenDevice);
            double scale = ScreenHelper.GetScaleForScreen(screen);
            int strip = (int)Math.Ceiling((NotchConstants.ClosedHeight + 2) * scale);
            _appBar.ReserveTop(screen.Bounds, strip);
        }
        else
        {
            _appBar.Release();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PositionFixedWindow();
        UpdateNotchVisuals();

        // Apply liquid glass if enabled at startup
        ApplyLiquidGlassEffect(_settings.Theme == AppTheme.LiquidGlass || _settings.UseLiquidGlassTheme);
        _themeService.ThemeChanged += () => Dispatcher.Invoke(() => ApplyLiquidGlassEffect(_themeService.IsLiquidGlass));

        // Register for per-frame rendering
        CompositionTarget.Rendering += OnRendering;

        // Mouse events on the notch path
        // Camera trigger zone events
        CameraTriggerZone.MouseEnter += OnCameraTriggerEnter;
        CameraTriggerZone.MouseMove += OnCameraTriggerMove;

        // Camera proximity background timer (120ms, throttled and running only when dock is Top and feature enabled)
        _cameraProximityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _cameraProximityTimer.Tick += OnCameraProximityTick;
        UpdateCameraProximityTimerState();

        // Idle memory trim timer (debounced 3 seconds after settling closed)
        _idleTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _idleTrimTimer.Tick += (_, _) =>
        {
            _idleTrimTimer.Stop();
            if (_vm.NotchState == NotchState.Closed && !_isDragging)
            {
                WindowHelper.TrimProcessWorkingSet();
            }
        };

        NotchPath.MouseEnter += OnNotchMouseEnter;
        NotchPath.MouseLeave += OnNotchMouseLeave;
        NotchPath.MouseLeftButtonDown += OnNotchMouseDown;
        NotchPath.MouseLeftButtonUp += OnNotchMouseUp;
        NotchPath.MouseMove += OnNotchMouseMove;

        // Also track mouse on content grid (so hovering content keeps it open)
        ContentGrid.MouseEnter += OnNotchMouseEnter;
        ContentGrid.MouseLeave += OnNotchMouseLeave;

        // Drag + click from content area (so vertical time label etc. are draggable)
        ContentGrid.PreviewMouseLeftButtonDown += OnNotchMouseDown;
        ContentGrid.PreviewMouseMove += OnContentGridMouseMove;
        ContentGrid.PreviewMouseLeftButtonUp += OnContentGridMouseUp;

        // Handle mouse leaving window during drag
        RootGrid.MouseLeave += OnRootGridMouseLeave;

        // Scroll on notch changes volume (Preview to catch before children)
        RootGrid.PreviewMouseWheel += OnNotchMouseWheel;

        // Right-click context menu
        NotchPath.MouseRightButtonDown += OnNotchRightClick;

        // Drag-drop for file shelf
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        // Initialize/bind shared services
        _ = InitializeServicesAsync();

        // Initialize plugin system (primary island only — a plugin's UIElement
        // can live in exactly one visual tree)
        if (_isPrimary)
            _ = InitializePluginsAsync();

        // Restore the persisted dock position (primary island only)
        if (_isPrimary && _settings.NotchDockPosition != NotchDock.Top)
            DockTo(_settings.NotchDockPosition);
    }

    private async System.Threading.Tasks.Task InitializeServicesAsync()
    {
        // Global one-time init happens once app-wide; every island awaits it
        await _services.EnsureInitializedAsync(_settings);

        // Bind views to services
        MusicPlayer.Bind(_mediaService);
        LiveActivity.Bind(_mediaService, _audioCaptureService);
        WaterWidget.Bind(_settings);
        SleepWidget.Bind(_settings);
        VitalsWidget.Bind(_services.Hardware, _batteryService, _services.ArmouryCrate);
        NotificationHistoryWidget.Bind(_services.Notifications);
        UpdateCarouselCards();

        // Inline settings
        InlineSettings.SettingsChanged += s =>
        {
            ApplySettings(s);
            SettingsUpdatedByUser?.Invoke(s);
        };
        InlineSettings.BackRequested += CollapseSettings;
        InlineSettings.ThemeChangeRequested += async themeSettings =>
        {
            _settings = themeSettings;
            await _themeService.ApplyWithTransition(themeSettings, NotchCanvas);
            SettingsUpdatedByUser?.Invoke(themeSettings);
        };

        // Vertical inline settings (separate instance for side dock)
        VerticalInlineSettings.SettingsChanged += s =>
        {
            ApplySettings(s);
            SettingsUpdatedByUser?.Invoke(s);
        };
        VerticalInlineSettings.BackRequested += CollapseSettings;
        VerticalInlineSettings.ThemeChangeRequested += async themeSettings =>
        {
            _settings = themeSettings;
            await _themeService.ApplyWithTransition(themeSettings, NotchCanvas);
            SettingsUpdatedByUser?.Invoke(themeSettings);
        };

        // Toggle clock/live-activity visibility based on music state
        _mediaService.MediaInfo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MediaInfo.HasMedia) or nameof(MediaInfo.IsPlaying))
                Dispatcher.Invoke(UpdateClosedContentVisibility);
        };
        _mediaService.SessionChanged += () => Dispatcher.Invoke(UpdateClosedContentVisibility);
        UpdateClosedContentVisibility();

        // Volume & Brightness
        HudOverlay.Bind(_volumeService, _brightnessService);
        VerticalHudOverlay.IsActive = false;
        VerticalHudOverlay.Bind(_volumeService, _brightnessService);

        // Battery
        BatteryIndicator.Bind(_batteryService);

        // Calendar
        CalendarPanel_View.Bind(_calendarService);
        CalendarPanel.Visibility = _settings.ShowCalendar ? Visibility.Visible : Visibility.Collapsed;

        // File Shelf
        ShelfPanel.Bind(_shelfService);
        _shelfService.ShelfChanged += () => Dispatcher.Invoke(UpdateShelfVisibility);

        // Webcam mirror
        if (_settings.ShowWebcam)
        {
            WebcamPanel.Visibility = Visibility.Visible;
            WebcamMirror.Bind(_webcamService);
        }
        // Intelligence & Notification monitors
        _services.Notifications.NotificationArrived += item => Dispatcher.Invoke(() => ShowNotification(item));
        _services.Approvals.ApprovalPromptArrived += req => Dispatcher.Invoke(() => ShowApprovalPrompt(req));
        _services.Approvals.ApprovalPromptSettled += _ => Dispatcher.Invoke(DismissApprovalPrompt);

        // Hardware Privacy Indicators (Camera & Microphone glowing dots)
        _services.Privacy.CameraStateChanged += (active, app) => Dispatcher.Invoke(() => UpdateCameraPrivacy(active, app));
        _services.Privacy.MicrophoneStateChanged += (active, app) => Dispatcher.Invoke(() => UpdateMicPrivacy(active, app));
        UpdateCameraPrivacy(_services.Privacy.IsCameraActive, _services.Privacy.CameraActiveApp);
        UpdateMicPrivacy(_services.Privacy.IsMicrophoneActive, _services.Privacy.MicrophoneActiveApp);

        // Focus & Countdown Timer Live Activity
        _services.Timer.Tick += (rem, prog) => Dispatcher.Invoke(() => UpdateTimerDisplay(rem, prog));
        _services.Timer.StateChanged += state => Dispatcher.Invoke(() => UpdateTimerState(state));
        _services.Timer.Completed += () => Dispatcher.Invoke(OnTimerCompleted);

        // Battery Charging Surge & Alerts
        _services.Battery.ChargingStateChanged += isCharging => Dispatcher.Invoke(() => OnChargingStateChanged(isCharging));

        // Hardware Lock Keys (Caps Lock & Num Lock)
        _services.LockKeys.CapsLockChanged += isCaps => Dispatcher.BeginInvoke(() => ShowLockState("CAPS LOCK", isCaps));
        _services.LockKeys.NumLockChanged += isNum => Dispatcher.BeginInvoke(() => ShowLockState("NUM LOCK", isNum));

        // USB & Removable Storage Drive Mount / Eject
        _services.Usb.UsbDriveConnected += (drive, label, bytes) => Dispatcher.BeginInvoke(() => ShowUsbDriveAlert(drive, label, bytes, true));
        _services.Usb.UsbDriveDisconnected += drive => Dispatcher.BeginInvoke(() => ShowUsbDriveAlert(drive, null, 0, false));

        // Armoury Crate / Performance Operating Mode Alert Pop-up
        _services.ArmouryCrate.OperatingModeChanged += mode => Dispatcher.BeginInvoke(() => ShowOperatingModeAlert(mode));

        // Middle-click on closed notch toggles media play/pause
        RootGrid.MouseDown += OnRootGridMouseDown;

        // Bind Spotlight Launcher
        _spotlightLauncher = new SpotlightLauncher();
        _spotlightLauncher.Bind(_services);

        // Bind Expanded Command Center
        CommandCenterPanel.Bind(_services);
        CommandCenterPanel.CloseRequested += CollapseCommandCenter;
        CommandCenterPanel.SpotlightRequested += OpenSpotlight;

        // Bind Tasks Widget with Auto-Tasks
        TasksWidget.Bind(_services);


        // Vertical side panel: wire media + clock
        _mediaService.MediaInfo.PropertyChanged += (_, e) =>
        {
            Dispatcher.Invoke(() => UpdateSideMediaContent(_mediaService.MediaInfo, e.PropertyName));
        };
        _mediaService.SessionChanged += () =>
        {
            Dispatcher.Invoke(() => UpdateSideMediaContent(_mediaService.MediaInfo, null));
        };

        // Side visualizer (skip work while it isn't on screen, e.g. top dock)
        _audioCaptureService.SpectrumUpdated += () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (SideVisualizer.IsVisible && _audioCaptureService.SpectrumData != null)
                    SideVisualizer.UpdateSpectrum(_audioCaptureService.SpectrumData);
            });
        };

        var sideClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        sideClockTimer.Tick += (_, _) => UpdateSideClock();
        sideClockTimer.Start();
        UpdateSideClock();
        UpdateSideMediaContent(_mediaService.MediaInfo, null);

        // Fullscreen detection — hide this island only when the fullscreen app
        // is on the SAME monitor (a game on screen 1 shouldn't hide screen 2's island)
        _fullscreenService.FullscreenChanged += isFs =>
        {
            Dispatcher.Invoke(() =>
            {
                bool hideThisIsland = isFs &&
                    _fullscreenService.FullscreenMonitor == ScreenHelper.GetMonitorHandle(this);

                NotchCanvas.Opacity = hideThisIsland ? 0 : 1;
                IsHitTestVisible = !hideThisIsland;
            });
        };

        // When HUD shows, expand notch and hide other content
        // Top dock HUD: expand width with Apple Dynamic Island liquid spring
        HudOverlay.HudShown += () => Dispatcher.Invoke(() =>
        {
            if (_vm.NotchState != NotchState.Open)
            {
                LiveActivity.Visibility = Visibility.Collapsed;
                ClockWidget.Visibility = Visibility.Collapsed;
                PluginClosedPanel.Visibility = Visibility.Collapsed;
                BatteryIndicator.Suppressed = true;
                _widthSpring.SetPhysics(0.32, 0.74);
                _widthSpring.AnimateTo(NotchConstants.HudWidth, _currentWidth);
            }
        });
        HudOverlay.HudDismissed += () => Dispatcher.Invoke(() =>
        {
            UpdateClosedContentVisibility();
            BatteryIndicator.Suppressed = false;
            if (_vm.NotchState != NotchState.Open)
            {
                _widthSpring.SetPhysics(0.32, 0.85);
                _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            }
        });

        // Side dock HUD: expand height only
        VerticalHudOverlay.HudShown += () => Dispatcher.Invoke(() =>
        {
            if (_vm.NotchState != NotchState.Open)
            {
                SideTimeSmall.Visibility = Visibility.Collapsed;
                VerticalPluginClosedPanel.Visibility = Visibility.Collapsed;
                _heightSpring.AnimateTo(NotchConstants.HudWidth, _currentHeight);
            }
        });
        VerticalHudOverlay.HudDismissed += () => Dispatcher.Invoke(() =>
        {
            SideTimeSmall.Visibility = Visibility.Visible;
            VerticalPluginClosedPanel.Visibility = Visibility.Visible;
            if (_vm.NotchState != NotchState.Open)
            {
                _heightSpring.AnimateTo(NotchConstants.ClosedWidth, _currentHeight);
            }
        });

        // Apply all persisted settings now that every component is bound —
        // without this, toggles like ShowMusicControls or ShowBattery only
        // take effect after the user touches a setting.
        ApplySettings(_settings);
    }

    private async System.Threading.Tasks.Task InitializePluginsAsync()
    {
        try
        {
            _pluginContext = new PluginContext(
                this, _vm,
                _mediaService, _themeService, _volumeService,
                _brightnessService, _batteryService, _calendarService,
                _audioCaptureService, _shelfService, _webcamService,
                _fullscreenService, _settings);
            _pluginContext.RegisterService(new PluginManagerNavigationService(OpenPluginManager));

            _pluginManager = new PluginManager(_pluginContext);
            _pluginLibraryService = new PluginLibraryService();

            await _pluginManager.LoadAllPluginsAsync();

            // Inject UI plugin elements into the notch
            InjectPluginUI();

            // Enable/disable now applies live — re-inject without a restart
            _pluginManager.PluginStateChanged += () => Dispatcher.Invoke(InjectPluginUI);

            // Wire settings "Manage Plugins" button
            InlineSettings.ManagePluginsRequested += OpenPluginManager;
            VerticalInlineSettings.ManagePluginsRequested += OpenPluginManager;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Plugin system initialization failed: {ex.Message}");
        }
    }

    private void InjectPluginUI()
    {
        if (_pluginManager == null) return;

        PluginClosedPanel.Children.Clear();
        PluginOpenPanel.Children.Clear();
        PluginAccessoryPanel.Children.Clear();
        PluginAccessoryPanel.Visibility = Visibility.Collapsed;
        VerticalPluginClosedPanel.Children.Clear();
        VerticalPluginOpenPanel.Children.Clear();

        foreach (var uiPlugin in _pluginManager.GetUIPlugins())
        {
            try
            {
                var closedElement = uiPlugin.GetUIElement(UIPluginLocation.ClosedContent);
                if (closedElement != null)
                {
                    if (_dock == NotchDock.Top)
                        PluginClosedPanel.Children.Add(closedElement);
                    else
                        VerticalPluginClosedPanel.Children.Add(closedElement);
                }

                if (_dock == NotchDock.Top)
                {
                    var accessoryElement = uiPlugin.GetUIElement(UIPluginLocation.OpenAccessory);
                    if (accessoryElement != null)
                    {
                        PluginAccessoryPanel.Children.Add(WrapAccessoryElement(accessoryElement));
                        PluginAccessoryPanel.Visibility = Visibility.Visible;
                    }

                    var openElement = uiPlugin.GetUIElement(UIPluginLocation.OpenContent);
                    if (openElement != null)
                        PluginOpenPanel.Children.Add(openElement);
                }
                else
                {
                    var verticalOpenElement = uiPlugin.GetUIElement(UIPluginLocation.VerticalOpenContent)
                        ?? uiPlugin.GetUIElement(UIPluginLocation.OpenContent);
                    if (verticalOpenElement != null)
                        VerticalPluginOpenPanel.Children.Add(verticalOpenElement);
                }

                // Notify initial state
                uiPlugin.OnNotchStateChanged(_vm.NotchState);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to inject UI for plugin {uiPlugin.Name}: {ex.Message}");
            }
        }

        RefreshPluginHostLayout();
    }

    private static UIElement WrapAccessoryElement(UIElement element)
    {
        var host = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        host.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(8, 4, 8, 4),
            Background = (System.Windows.Media.Brush?)Application.Current?.TryFindResource("SeparatorBrush")
                ?? new SolidColorBrush(Color.FromRgb(72, 72, 76))
        });
        host.Children.Add(element);
        return host;
    }

    private void NotifyPluginsStateChanged(NotchState state)
    {
        if (_pluginManager == null) return;
        foreach (var uiPlugin in _pluginManager.GetUIPlugins())
        {
            try { uiPlugin.OnNotchStateChanged(state); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin state notification error ({uiPlugin.Name}): {ex.Message}");
            }
        }
    }

    private void OpenPluginManager()
    {
        OpenPluginManager(null);
    }

    private void OpenPluginManager(string? focusPluginId)
    {
        if (_pluginManager == null || _pluginLibraryService == null) return;

        // Dismiss notch overlay so it does not cover or block the plugin manager window
        if (_isSettingsExpanded)
        {
            CollapseSettings();
        }
        if (_isCommandCenterExpanded)
        {
            CollapseCommandCenter();
        }
        TransitionToClose();

        // If a plugin manager instance is already open, activate and focus it instead of stacking multiple windows
        if (_pluginManagerWindow != null && _pluginManagerWindow.IsLoaded)
        {
            if (_pluginManagerWindow.WindowState == WindowState.Minimized)
            {
                _pluginManagerWindow.WindowState = WindowState.Normal;
            }

            if (!string.IsNullOrWhiteSpace(focusPluginId) && _pluginManagerWindow.Content is PluginManagerView existingView)
            {
                existingView.OpenConfigurationForPlugin(focusPluginId);
            }

            _pluginManagerWindow.Activate();
            _pluginManagerWindow.Focus();
            return;
        }

        var pluginView = new PluginManagerView(_pluginManager, _pluginLibraryService, focusPluginId);
        var window = new Window
        {
            Title = "X-Notch Plugin Manager",
            Width = 980,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
            Content = pluginView
        };

        window.Closed += (_, _) => _pluginManagerWindow = null;
        _pluginManagerWindow = window;

        window.Show();
        window.Activate();
        window.Focus();
    }

    // A resting shadow keeps the island visible on same-color wallpapers
    // (e.g. a white notch on a white background). ShowShadow disables all of it.
    private double ClosedShadowOpacity => _settings.ShowShadow ? 0.6 : 0.0;
    private double OpenShadowOpacity => _settings.ShowShadow ? 0.95 : 0.0;
    private double DragShadowOpacity => _settings.ShowShadow ? 0.7 : 0.0;

    private void UpdateClosedContentVisibility()
    {
        var info = _mediaService.MediaInfo;
        bool hasMusic = _settings.ShowMusicControls && info.HasMedia && info.IsPlaying;
        LiveActivity.Visibility = hasMusic ? Visibility.Visible : Visibility.Collapsed;
        ClockWidget.Visibility = hasMusic ? Visibility.Collapsed : Visibility.Visible;
        PluginClosedPanel.Visibility = Visibility.Visible;
    }

    #region Vertical Side Content

    private void UpdateSideClock()
    {
        var now = DateTime.Now;
        SideTimeText.Text = now.ToString("HH:mm");
        SideDateText.Text = now.ToString("ddd d", System.Globalization.CultureInfo.CurrentCulture);
        SideTimeSmall.Text = now.ToString("HH:mm");
    }

    private void UpdateSideMediaContent(MediaInfo info, string? prop)
    {
        if (prop == null || prop == nameof(info.Title))
            SideSongTitle.Text = info.Title;
        if (prop == null || prop == nameof(info.Artist))
            SideArtistName.Text = info.Artist;
        if (prop == null || prop == nameof(info.AlbumArt))
            SideAlbumArt.Source = info.AlbumArt;
        if (prop == null || prop == nameof(info.IsPlaying))
            SidePlayIcon.Text = info.IsPlaying ? "⏸" : "▶";
        if (prop == null || prop == nameof(info.HasMedia))
            SideMusicPanel.Visibility = info.HasMedia ? Visibility.Visible : Visibility.Collapsed;

        // Progress bar + time
        if (prop == null || prop == nameof(info.Position) || prop == nameof(info.ProgressPercent))
        {
            SideElapsedText.Text = FormatTimeSpan(info.Position);
            UpdateSideProgressBar(info.ProgressPercent);
        }
        if (prop == null || prop == nameof(info.Duration))
            SideDurationText.Text = FormatTimeSpan(info.Duration);

        // Visualizer bar color from album dominant color
        if (prop == null || prop == nameof(info.DominantColor))
        {
            var color = info.DominantColor;
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            SideVisualizer.BarColor = brightness < 0.3 ? System.Windows.Media.Colors.White : color;
        }
    }

    private void UpdateSideProgressBar(double percent)
    {
        if (SideProgressFill.Parent is System.Windows.Controls.Border parent)
        {
            double parentWidth = parent.ActualWidth;
            // Only update if parent has valid width to prevent visual glitches
            if (parentWidth > 0 && !double.IsNaN(parentWidth) && !double.IsInfinity(parentWidth))
            {
                double targetWidth = Math.Max(0, Math.Min(percent * parentWidth, parentWidth));
                SideProgressFill.Width = targetWidth;
            }
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private async void OnSidePlayClick(object sender, RoutedEventArgs e) =>
        await _mediaService.PlayPauseAsync();

    private async void OnSidePrevClick(object sender, RoutedEventArgs e) =>
        await _mediaService.PreviousAsync();

    private async void OnSideNextClick(object sender, RoutedEventArgs e) =>
        await _mediaService.NextAsync();

    private async void OnSideProgressBarClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border bar) return;

        double barWidth = bar.ActualWidth;
        // Validate bar width before calculating seek position
        if (barWidth <= 0 || double.IsNaN(barWidth) || double.IsInfinity(barWidth)) return;

        var pos = e.GetPosition(bar);
        double percent = Math.Clamp(pos.X / barWidth, 0, 1);
        await _mediaService.SeekToAsync(percent);
    }

    private void OnSideSettingsClick(object sender, RoutedEventArgs e)
    {
        // In vertical view, expand settings on the right side dynamically
        if (_isSettingsExpanded)
            CollapseSettings();
        else
            ExpandSettings();
    }

    #endregion

    public void OpenSettings()
    {
        // First ensure the notch is open
        if (_vm.NotchState != NotchState.Open)
            TransitionToOpen();

        ExpandSettings();
    }

    private bool _isCommandCenterExpanded;

    public void OnCommandCenterButtonClick(object sender, RoutedEventArgs e)
    {
        if (_vm.NotchState != NotchState.Open)
            TransitionToOpen();

        if (_isCommandCenterExpanded)
            CollapseCommandCenter();
        else
            ExpandCommandCenter();
    }

    private void ExpandCommandCenter()
    {
        if (_isSettingsExpanded)
            CollapseSettings();

        _isCommandCenterExpanded = true;

        if (_dock == NotchDock.Top)
        {
            CommandCenterPanel.Visibility = Visibility.Visible;

            double targetH = GetTopCommandCenterHeight();
            _heightSpring.Response = 0.42;
            _heightSpring.DampingFraction = 0.80;
            _heightSpring.AnimateTo(targetH, _currentHeight);
            _widthSpring.AnimateTo(GetOpenWidth(), _currentWidth);
        }
    }

    private void CollapseCommandCenter()
    {
        _isCommandCenterExpanded = false;

        if (_dock == NotchDock.Top)
        {
            CommandCenterPanel.Visibility = Visibility.Collapsed;

            double targetH = GetTopOpenHeight();
            _heightSpring.Response = 0.42;
            _heightSpring.DampingFraction = 0.80;
            _heightSpring.AnimateTo(targetH, _currentHeight);
            _widthSpring.AnimateTo(GetOpenWidth(), _currentWidth);
        }
    }

    private double GetTopCommandCenterHeight()
    {
        double baseHeight = ShouldReserveShelfSpace()
            ? 520
            : 440;

        return baseHeight + GetMeasuredPanelHeight(PluginOpenPanel, Math.Max(220, GetOpenWidth() - 48));
    }

    private void OnSettingsGearClick(object sender, RoutedEventArgs e)
    {
        if (_isSettingsExpanded)
            CollapseSettings();
        else
            ExpandSettings();
    }

    private void ExpandSettings()
    {
        if (_isCommandCenterExpanded)
            CollapseCommandCenter();

        _isSettingsExpanded = true;

        if (_dock == NotchDock.Top)
        {
            // Top dock: Load settings and grow the notch taller (slide down)
            InlineSettings.LoadSettings(_settings);
            InlineSettings.ResetScroll();
            InlineSettings.Visibility = Visibility.Visible;

            double targetH = GetTopSettingsHeight();
            _heightSpring.Response = 0.42;
            _heightSpring.DampingFraction = 0.80;
            _heightSpring.AnimateTo(targetH, _currentHeight);
        }
        else
        {
            // Side dock: Load settings in vertical panel and expand width
            VerticalInlineSettings.LoadSettings(_settings);
            VerticalInlineSettings.ResetScroll();
            VerticalSettingsPanel.Visibility = Visibility.Visible;

            double targetW = NotchConstants.SideOpenWidth + 280 + NotchConstants.OpenTopRadius;
            _widthSpring.Response = 0.42;
            _widthSpring.DampingFraction = 0.80;
            _widthSpring.AnimateTo(targetW, _currentWidth);
        }
    }

    private void CollapseSettings()
    {
        _isSettingsExpanded = false;

        if (_dock == NotchDock.Top)
        {
            // Top dock: Hide settings and shrink back to normal open height
            InlineSettings.Visibility = Visibility.Collapsed;

            double targetH = GetTopOpenHeight();
            _heightSpring.Response = 0.42;
            _heightSpring.DampingFraction = 0.80;
            _heightSpring.AnimateTo(targetH, _currentHeight);
        }
        else
        {
            // Side dock: Hide settings panel and shrink width back
            VerticalSettingsPanel.Visibility = Visibility.Collapsed;

            _widthSpring.Response = 0.42;
            _widthSpring.DampingFraction = 0.80;
            _widthSpring.AnimateTo(NotchConstants.SideOpenWidth, _currentWidth);
        }
    }

    // Removed OpenSettingsWindow - now using integrated settings panel

    /// <summary>
    /// Position the window once: centered at top of primary screen,
    /// sized to fit the largest notch state (open) + shadow padding.
    /// The window never moves or resizes after this.
    /// </summary>
    private void PositionFixedWindow()
    {
        UpdateFixedWindowBounds();
        RefreshScreenBounds();

        int w = (int)Math.Round(_fixedWindowWidth * _screenScale);
        int h = (int)Math.Round(_fixedWindowHeight * _screenScale);
        int x = _screenPhysical.Left + (_screenPhysical.Width - w) / 2;
        int y = _screenPhysical.Top - (int)Math.Round(2 * _screenScale);
        WindowHelper.SetWindowRectPhysical(this, x, y, w, h);
    }

    /// <summary>
    /// Resolves this island's screen device to physical bounds + DPI scale.
    /// Falls back to the primary screen when the device is missing.
    /// </summary>
    private void RefreshScreenBounds()
    {
        var screen = ScreenHelper.FindScreenByDevice(_screenDevice);
        _screenDevice = screen.DeviceName;
        _screenPhysical = screen.Bounds;
        _screenScale = ScreenHelper.GetScaleForScreen(screen);
    }

    /// <summary>
    /// Re-validates the screen and repositions after a display-topology change
    /// (monitor plugged/unplugged, resolution change).
    /// </summary>
    public void HandleDisplayChange()
    {
        if (_isDragging) return;
        RefreshScreenBounds();
        RepositionForDock();
        UpdateSpaceReservation();
    }

    private void UpdateFixedWindowBounds()
    {
        double padding = NotchConstants.WindowPadding;
        double maxBaseTopOpenWidth = new[]
        {
            NotchConstants.OpenWidth,
            580.0,
            NotchConstants.OpenWidthWithCalendar,
            NotchConstants.OpenWidthWithWebcam,
            NotchConstants.OpenWidthWithCalendarAndWebcam
        }.Max();
        double maxTopOpenWidth = maxBaseTopOpenWidth
            + GetMeasuredPanelWidth(PluginAccessoryPanel, NotchConstants.OpenHeight);

        double maxTopHeight = Math.Max(Math.Max(GetTopOpenHeight(), GetTopSettingsHeight()), GetTopCommandCenterHeight());
        double sideExpandedWidth = NotchConstants.SideOpenWidth + 280 + NotchConstants.OpenTopRadius;
        double maxShortAxis = Math.Max(maxTopHeight, sideExpandedWidth);
        double maxTallAxis = Math.Max(maxTopOpenWidth, GetSideOpenHeight());

        _fixedWindowWidth = maxTallAxis + padding * 2;
        _fixedWindowHeight = maxShortAxis + padding + 10;
    }

    private void RefreshPluginHostLayout()
    {
        UpdateFixedWindowBounds();

        if (!IsLoaded || _isDragging)
            return;

        RepositionForDock();

        if (_vm.NotchState != NotchState.Open)
            return;

        if (_dock == NotchDock.Top)
        {
            _widthSpring.AnimateTo(GetOpenWidth(), _currentWidth);

            var targetHeight = _isSettingsExpanded
                ? GetTopSettingsHeight()
                : _isCommandCenterExpanded
                    ? GetTopCommandCenterHeight()
                    : GetTopOpenHeight();

            _heightSpring.AnimateTo(targetHeight, _currentHeight);
            return;
        }

        _heightSpring.AnimateTo(GetSideOpenHeight(), _currentHeight);
    }

    private bool ShouldReserveShelfSpace()
    {
        return _shelfService.HasItems || ShelfPanel.Visibility == Visibility.Visible;
    }

    private double GetTopOpenHeight()
    {
        if (_isCommandCenterExpanded)
            return GetTopCommandCenterHeight();

        double baseHeight = ShouldReserveShelfSpace()
            ? NotchConstants.OpenHeightWithShelf
            : NotchConstants.OpenHeight;

        return baseHeight + GetMeasuredPanelHeight(PluginOpenPanel, Math.Max(220, GetOpenWidth() - 48));
    }

    private double GetTopSettingsHeight()
    {
        double baseHeight = ShouldReserveShelfSpace()
            ? NotchConstants.OpenSettingsHeightWithShelf
            : NotchConstants.OpenSettingsHeight;

        return baseHeight + GetMeasuredPanelHeight(PluginOpenPanel, Math.Max(220, GetOpenWidth() - 48));
    }

    private double GetSideOpenHeight()
    {
        return NotchConstants.SideOpenHeight + GetMeasuredPanelHeight(VerticalPluginOpenPanel, 136);
    }

    private static double GetMeasuredPanelHeight(System.Windows.Controls.Panel panel, double availableWidth)
    {
        if (panel.Children.Count == 0)
            return 0;

        panel.Measure(new Size(availableWidth, double.PositiveInfinity));
        return Math.Ceiling(panel.DesiredSize.Height);
    }

    private static double GetMeasuredPanelWidth(System.Windows.Controls.Panel panel, double availableHeight)
    {
        if (panel.Children.Count == 0)
            return 0;

        panel.Measure(new Size(double.PositiveInfinity, availableHeight));
        return Math.Ceiling(panel.DesiredSize.Width);
    }

    #region Animation Loop

    private void OnRendering(object? sender, EventArgs e)
    {
        bool anyAnimating = false;

        if (_widthSpring.IsAnimating)
        {
            _currentWidth = _widthSpring.Tick();
            anyAnimating = true;
        }
        if (_heightSpring.IsAnimating)
        {
            _currentHeight = _heightSpring.Tick();
            anyAnimating = true;
        }
        if (_topRadiusSpring.IsAnimating)
        {
            _currentTopRadius = _topRadiusSpring.Tick();
            anyAnimating = true;
        }
        if (_bottomRadiusSpring.IsAnimating)
        {
            _currentBottomRadius = _bottomRadiusSpring.Tick();
            anyAnimating = true;
        }
        if (_shadowOpacitySpring.IsAnimating)
        {
            _currentShadowOpacity = _shadowOpacitySpring.Tick();
            anyAnimating = true;
        }
        if (_contentOpacitySpring.IsAnimating)
        {
            _currentContentOpacity = _contentOpacitySpring.Tick();
            anyAnimating = true;
        }
        if (_contentScaleSpring.IsAnimating)
        {
            _currentContentScale = _contentScaleSpring.Tick();
            OpenContentScale.ScaleX = _currentContentScale;
            OpenContentScale.ScaleY = _currentContentScale;
            double closedScale = Math.Clamp(2.0 - _currentContentScale, 0.85, 1.08);
            ClosedContentScale.ScaleX = closedScale;
            ClosedContentScale.ScaleY = closedScale;
            anyAnimating = true;
        }
        if (_auraOpacitySpring.IsAnimating)
        {
            _currentAuraOpacity = _auraOpacitySpring.Tick();
            AmbientAuraPath.Opacity = 0.0;
            AmbientAuraPath.Visibility = Visibility.Collapsed;
        }

        if (_notificationScaleSpring.IsAnimating || _notificationTranslateSpring.IsAnimating ||
            _notificationOpacitySpring.IsAnimating || _notificationIconScaleSpring.IsAnimating)
        {
            _currentNotificationScale = _notificationScaleSpring.Tick();
            _currentNotificationTranslate = _notificationTranslateSpring.Tick();
            _currentNotificationOpacity = _notificationOpacitySpring.Tick();
            _currentNotificationIconScale = _notificationIconScaleSpring.Tick();

            NotificationCapsuleScale.ScaleX = _currentNotificationScale;
            NotificationCapsuleScale.ScaleY = _currentNotificationScale;
            NotificationCapsuleTranslate.Y = _currentNotificationTranslate;
            NotificationCapsule.Opacity = _currentNotificationOpacity;
            NotificationIconScale.ScaleX = _currentNotificationIconScale;
            NotificationIconScale.ScaleY = _currentNotificationIconScale;
            anyAnimating = true;
        }

        if (_approvalScaleSpring.IsAnimating || _approvalTranslateSpring.IsAnimating ||
            _approvalOpacitySpring.IsAnimating || _approvalIconScaleSpring.IsAnimating)
        {
            _currentApprovalScale = _approvalScaleSpring.Tick();
            _currentApprovalTranslate = _approvalTranslateSpring.Tick();
            _currentApprovalOpacity = _approvalOpacitySpring.Tick();
            _currentApprovalIconScale = _approvalIconScaleSpring.Tick();

            AgentApprovalScale.ScaleX = _currentApprovalScale;
            AgentApprovalScale.ScaleY = _currentApprovalScale;
            AgentApprovalTranslate.Y = _currentApprovalTranslate;
            AgentApprovalCapsule.Opacity = _currentApprovalOpacity;
            AgentApprovalIconScale.ScaleX = _currentApprovalIconScale;
            AgentApprovalIconScale.ScaleY = _currentApprovalIconScale;
            anyAnimating = true;
        }

        if (_powerSurgeScaleSpring.IsAnimating || _powerSurgeTranslateSpring.IsAnimating || _powerSurgeOpacitySpring.IsAnimating)
        {
            _currentPowerSurgeScale = _powerSurgeScaleSpring.Tick();
            _currentPowerSurgeTranslate = _powerSurgeTranslateSpring.Tick();
            _currentPowerSurgeOpacity = _powerSurgeOpacitySpring.Tick();

            PowerSurgeScale.ScaleX = _currentPowerSurgeScale;
            PowerSurgeScale.ScaleY = _currentPowerSurgeScale;
            PowerSurgeTranslate.Y = _currentPowerSurgeTranslate;
            PowerSurgeCapsule.Opacity = _currentPowerSurgeOpacity;
            anyAnimating = true;
        }

        if (_lockStateScaleSpring.IsAnimating || _lockStateTranslateSpring.IsAnimating || _lockStateOpacitySpring.IsAnimating)
        {
            _currentLockStateScale = _lockStateScaleSpring.Tick();
            _currentLockStateTranslate = _lockStateTranslateSpring.Tick();
            _currentLockStateOpacity = _lockStateOpacitySpring.Tick();

            LockStateScale.ScaleX = _currentLockStateScale;
            LockStateScale.ScaleY = _currentLockStateScale;
            LockStateTranslate.Y = _currentLockStateTranslate;
            LockStateCapsule.Opacity = _currentLockStateOpacity;
            anyAnimating = true;
        }

        if (_usbDriveScaleSpring.IsAnimating || _usbDriveTranslateSpring.IsAnimating || _usbDriveOpacitySpring.IsAnimating)
        {
            _currentUsbDriveScale = _usbDriveScaleSpring.Tick();
            _currentUsbDriveTranslate = _usbDriveTranslateSpring.Tick();
            _currentUsbDriveOpacity = _usbDriveOpacitySpring.Tick();

            UsbDriveScale.ScaleX = _currentUsbDriveScale;
            UsbDriveScale.ScaleY = _currentUsbDriveScale;
            UsbDriveTranslate.Y = _currentUsbDriveTranslate;
            UsbDriveCapsule.Opacity = _currentUsbDriveOpacity;
            anyAnimating = true;
        }

        if (_operatingModeScaleSpring.IsAnimating || _operatingModeTranslateSpring.IsAnimating || _operatingModeOpacitySpring.IsAnimating)
        {
            _currentOperatingModeScale = _operatingModeScaleSpring.Tick();
            _currentOperatingModeTranslate = _operatingModeTranslateSpring.Tick();
            _currentOperatingModeOpacity = _operatingModeOpacitySpring.Tick();

            OperatingModeScale.ScaleX = _currentOperatingModeScale;
            OperatingModeScale.ScaleY = _currentOperatingModeScale;
            OperatingModeTranslate.Y = _currentOperatingModeTranslate;
            OperatingModeCapsule.Opacity = _currentOperatingModeOpacity;
            anyAnimating = true;
        }

        if (anyAnimating)
        {
            _wasAnimating = true;
            _idleTrimTimer?.Stop();
            UpdateNotchVisuals();
        }
        else if (_wasAnimating)
        {
            _wasAnimating = false;
            if (_vm.NotchState == NotchState.Closed && !_isDragging)
            {
                _idleTrimTimer?.Stop();
                _idleTrimTimer?.Start();
            }
        }
    }

    private void UpdateNotchVisuals()
    {
        double w = _currentWidth;
        double h = _currentHeight;
        double topR = _currentTopRadius;
        double bottomR = _currentBottomRadius;

        // Generate notch geometry
        var rect = new Rect(0, 0, w, h);
        Geometry geometry;

        if (_isDragging)
            geometry = NotchShape.CreateNotchGeometry(rect, topR, bottomR);
        else if (_dock == NotchDock.Left)
            geometry = NotchShape.CreateSideNotchGeometry(rect, topR, bottomR, isLeft: true);
        else if (_dock == NotchDock.Right)
            geometry = NotchShape.CreateSideNotchGeometry(rect, topR, bottomR, isLeft: false);
        else
            geometry = NotchShape.CreateNotchGeometry(rect, topR, bottomR);

        NotchPath.Data = geometry;
        ShadowPath.Data = geometry;
        AmbientAuraPath.Data = geometry;
        BackdropBlurPath.Data = geometry;
        GlassSpecularPath.Data = geometry;
        GlassBorderPath.Data = geometry;
        GlassInnerShadowPath.Data = geometry;

        // Ambient Aura chromatic illumination — permanently disabled for clean borderless cutout
        AmbientAuraPath.Opacity = 0.0;
        AmbientAuraPath.Visibility = Visibility.Collapsed;

        double windowW = ActualWidth > 0 ? ActualWidth : Width;
        double windowH = ActualHeight > 0 ? ActualHeight : Height;

        double offsetX, offsetY;

        if (_isDragging)
        {
            // During drag the mouse handler sizes the window (physical px);
            // here we only lay content out against the DIP pill size
            double dragW = NotchConstants.ClosedWidth * 0.8;
            double dragH = NotchConstants.ClosedHeight * 0.8;
            windowW = dragW;
            windowH = dragH;
            offsetX = 0;
            offsetY = 0;

            // Position is already updated in OnNotchMouseMove to follow cursor smoothly
            // Don't recalculate here to avoid visual jitter
        }
        else if (_dock == NotchDock.Left)
        {
            offsetX = 0; // Anchored to left edge
            offsetY = (windowH - h) / 2;
        }
        else if (_dock == NotchDock.Right)
        {
            offsetX = windowW - w; // Anchored to right edge
            offsetY = (windowH - h) / 2;
        }
        else
        {
            // Top dock: centered horizontally at top
            offsetX = (windowW - w) / 2;
            offsetY = 0;
        }

        System.Windows.Controls.Canvas.SetLeft(NotchPath, offsetX);
        System.Windows.Controls.Canvas.SetTop(NotchPath, offsetY);
        System.Windows.Controls.Canvas.SetLeft(ShadowPath, offsetX);
        System.Windows.Controls.Canvas.SetTop(ShadowPath, offsetY);
        System.Windows.Controls.Canvas.SetLeft(AmbientAuraPath, offsetX);
        System.Windows.Controls.Canvas.SetTop(AmbientAuraPath, offsetY);
        System.Windows.Controls.Canvas.SetLeft(BackdropBlurPath, offsetX);
        System.Windows.Controls.Canvas.SetTop(BackdropBlurPath, offsetY);
        System.Windows.Controls.Canvas.SetLeft(GlassSpecularPath, offsetX);
        System.Windows.Controls.Canvas.SetTop(GlassSpecularPath, offsetY);
        System.Windows.Controls.Canvas.SetLeft(GlassBorderPath, offsetX);
        System.Windows.Controls.Canvas.SetTop(GlassBorderPath, offsetY);
        System.Windows.Controls.Canvas.SetLeft(GlassInnerShadowPath, offsetX);
        System.Windows.Controls.Canvas.SetTop(GlassInnerShadowPath, offsetY);

        // Shadow opacity (disabled by default for seamless borderless cutout)
        ShadowPath.Opacity = _settings.ShowShadow ? _currentShadowOpacity : 0.0;
        ShadowPath.Visibility = _settings.ShowShadow && ShadowPath.Opacity > 0.01 ? Visibility.Visible : Visibility.Collapsed;

        // Content grid: positioned inside the notch shape (inset from corners)
        double contentLeft, contentTop, contentWidth, contentHeight;

        if (_dock == NotchDock.Left)
        {
            contentLeft = offsetX + topR;
            contentTop = offsetY + topR + bottomR;
            contentWidth = Math.Max(0, w - topR);
            contentHeight = Math.Max(0, h - 2 * (topR + bottomR));
        }
        else if (_dock == NotchDock.Right)
        {
            contentLeft = offsetX;
            contentTop = offsetY + topR + bottomR;
            contentWidth = Math.Max(0, w - topR);
            contentHeight = Math.Max(0, h - 2 * (topR + bottomR));
        }
        else
        {
            contentLeft = offsetX + topR + 6;
            contentTop = offsetY + 1;
            contentWidth = Math.Max(0, w - 2 * (topR + 6));
            contentHeight = Math.Max(0, h - 2);
        }

        System.Windows.Controls.Canvas.SetLeft(ContentGrid, contentLeft);
        System.Windows.Controls.Canvas.SetTop(ContentGrid, contentTop);
        ContentGrid.Width = contentWidth;
        ContentGrid.Height = contentHeight;

        // Content opacity & strict visibility — prevent any closed/open text overlay
        if (_dock != NotchDock.Top)
        {
            VerticalOpenContent.Opacity = _currentContentOpacity;
            VerticalClosedContent.Opacity = 1.0 - _currentContentOpacity;
            VerticalOpenContent.Visibility = _currentContentOpacity > 0.02 ? Visibility.Visible : Visibility.Collapsed;
            VerticalClosedContent.Visibility = (1.0 - _currentContentOpacity) > 0.02 ? Visibility.Visible : Visibility.Collapsed;
            OpenContent.Opacity = 0;
            ClosedContent.Opacity = 0;
            OpenContent.Visibility = Visibility.Collapsed;
            ClosedContent.Visibility = Visibility.Collapsed;
        }
        else
        {
            OpenContent.Opacity = _currentContentOpacity;
            ClosedContent.Opacity = 1.0 - _currentContentOpacity;
            OpenContent.Visibility = _currentContentOpacity > 0.02 ? Visibility.Visible : Visibility.Collapsed;
            ClosedContent.Visibility = (1.0 - _currentContentOpacity) > 0.02 ? Visibility.Visible : Visibility.Collapsed;
            VerticalOpenContent.Opacity = 0;
            VerticalClosedContent.Opacity = 0;
            VerticalOpenContent.Visibility = Visibility.Collapsed;
            VerticalClosedContent.Visibility = Visibility.Collapsed;
        }

        // Invisible layers must not swallow clicks (WPF hit-tests opacity-0 elements)
        OpenContent.IsHitTestVisible = OpenContent.Opacity > 0.5;
        ClosedContent.IsHitTestVisible = ClosedContent.Opacity > 0.5;
        VerticalOpenContent.IsHitTestVisible = VerticalOpenContent.Opacity > 0.5;
        VerticalClosedContent.IsHitTestVisible = VerticalClosedContent.Opacity > 0.5;

        // Camera hover trigger zone — position at top center
        double cameraZoneW = 280;
        double cameraZoneH = 38;
        System.Windows.Controls.Canvas.SetLeft(CameraTriggerZone, (windowW - cameraZoneW) / 2);
        System.Windows.Controls.Canvas.SetTop(CameraTriggerZone, 0);
        CameraTriggerZone.Width = cameraZoneW;
        CameraTriggerZone.Height = cameraZoneH;
        CameraTriggerZone.IsHitTestVisible = (_dock == NotchDock.Top && _vm.NotchState != NotchState.Open);

        // Dynamic Island Alerts (Approvals, Notifications, Power Surge, Lock State, USB, Operating Mode)
        bool showApproval = (_activeApproval != null || _approvalOpacitySpring.IsAnimating || _currentApprovalOpacity > 0.01) && _vm.NotchState != NotchState.Open;
        bool showNotification = (_activeNotification != null || _notificationOpacitySpring.IsAnimating || _currentNotificationOpacity > 0.01) && !showApproval && _vm.NotchState != NotchState.Open;
        bool showPowerSurge = (_powerSurgeActive || _powerSurgeOpacitySpring.IsAnimating || _currentPowerSurgeOpacity > 0.01) && !showApproval && !showNotification && _vm.NotchState != NotchState.Open;
        bool showLockState = (_lockStateActive || _lockStateOpacitySpring.IsAnimating || _currentLockStateOpacity > 0.01) && !showApproval && !showNotification && !showPowerSurge && _vm.NotchState != NotchState.Open;
        bool showUsbDrive = (_usbDriveActive || _usbDriveOpacitySpring.IsAnimating || _currentUsbDriveOpacity > 0.01) && !showApproval && !showNotification && !showPowerSurge && !showLockState && _vm.NotchState != NotchState.Open;
        bool showOperatingMode = (_operatingModeActive || _operatingModeOpacitySpring.IsAnimating || _currentOperatingModeOpacity > 0.01) && !showApproval && !showNotification && !showPowerSurge && !showLockState && !showUsbDrive && _vm.NotchState != NotchState.Open;

        AgentApprovalCapsule.Visibility = showApproval ? Visibility.Visible : Visibility.Collapsed;
        NotificationCapsule.Visibility = showNotification ? Visibility.Visible : Visibility.Collapsed;
        PowerSurgeCapsule.Visibility = showPowerSurge ? Visibility.Visible : Visibility.Collapsed;
        LockStateCapsule.Visibility = showLockState ? Visibility.Visible : Visibility.Collapsed;
        UsbDriveCapsule.Visibility = showUsbDrive ? Visibility.Visible : Visibility.Collapsed;
        OperatingModeCapsule.Visibility = showOperatingMode ? Visibility.Visible : Visibility.Collapsed;

        AgentApprovalCapsule.IsHitTestVisible = showApproval && _currentApprovalOpacity > 0.5;
        NotificationCapsule.IsHitTestVisible = showNotification && _currentNotificationOpacity > 0.5;
        PowerSurgeCapsule.IsHitTestVisible = showPowerSurge && _currentPowerSurgeOpacity > 0.5;
        LockStateCapsule.IsHitTestVisible = showLockState && _currentLockStateOpacity > 0.5;
        UsbDriveCapsule.IsHitTestVisible = showUsbDrive && _currentUsbDriveOpacity > 0.5;
        OperatingModeCapsule.IsHitTestVisible = showOperatingMode && _currentOperatingModeOpacity > 0.5;

        double maxCapsuleOpacity = Math.Max(_currentNotificationOpacity,
            Math.Max(_currentApprovalOpacity,
            Math.Max(_currentPowerSurgeOpacity,
            Math.Max(_currentLockStateOpacity,
            Math.Max(_currentUsbDriveOpacity, _currentOperatingModeOpacity)))));

        if (maxCapsuleOpacity > 0.01)
        {
            ClosedContent.Opacity = Math.Max(0.0, 1.0 - maxCapsuleOpacity);
            ClosedContent.IsHitTestVisible = ClosedContent.Opacity > 0.5;
        }

        // Canvas covers the full window
        NotchCanvas.Width = windowW;
        NotchCanvas.Height = windowH;
    }

    #endregion

    #region State Transitions

    /// <summary>
    /// Applies an open/close originating from another island (sync mode)
    /// without re-broadcasting it.
    /// </summary>
    public void ApplySyncedState(NotchState state)
    {
        if (_isDragging || _vm.NotchState == state) return;

        _applyingSyncedState = true;
        try
        {
            if (state == NotchState.Open)
                TransitionToOpen();
            else if (state == NotchState.Closed)
                TransitionToClose();
        }
        finally
        {
            _applyingSyncedState = false;
        }
    }

    private void TransitionToOpen()
    {
        _cameraPopUpTimer?.Stop();
        _isCameraPopUpActive = false;
        if (_vm.NotchState == NotchState.Open) return;
        _vm.Open();
        CalendarPanel_View?.ResetToGlanceView();
        WindowHelper.EnableActivation(this);
        NotifyPluginsStateChanged(NotchState.Open);
        if (!_applyingSyncedState)
            UserStateChanged?.Invoke(NotchState.Open);

        _widthSpring.Response = 0.38;
        _widthSpring.DampingFraction = 0.82;
        _heightSpring.Response = 0.36;
        _heightSpring.DampingFraction = 0.80;

        if (_dock == NotchDock.Top)
        {
            double openW = GetOpenWidth();
            double openH = GetTopOpenHeight();

            _widthSpring.AnimateTo(openW, _currentWidth);
            _heightSpring.AnimateTo(openH, _currentHeight);
        }
        else
        {
            // Side dock: width = protrusion from edge, height = tall axis
            double openW = NotchConstants.SideOpenWidth;
            double openH = GetSideOpenHeight();

            _widthSpring.AnimateTo(openW, _currentWidth);
            _heightSpring.AnimateTo(openH, _currentHeight);
        }

        _topRadiusSpring.AnimateTo(NotchConstants.OpenTopRadius, _currentTopRadius);
        _bottomRadiusSpring.AnimateTo(NotchConstants.OpenBottomRadius, _currentBottomRadius);
        _shadowOpacitySpring.AnimateTo(OpenShadowOpacity, _currentShadowOpacity);
        _contentOpacitySpring.AnimateTo(1.0, _currentContentOpacity);
        _contentScaleSpring.AnimateTo(1.0, _currentContentScale);
        _auraOpacitySpring.AnimateTo(0.65, _currentAuraOpacity);
    }

    private void TransitionToPeek()
    {
        if (_vm.NotchState == NotchState.Peeking || _vm.NotchState == NotchState.Open) return;
        _vm.Peek();
        NotifyPluginsStateChanged(NotchState.Peeking);

        bool hasMusic = _settings.ShowMusicControls && _mediaService.MediaInfo.HasMedia && _mediaService.MediaInfo.IsPlaying;
        if (hasMusic)
            LiveActivity.IsPeeking = true;

        _widthSpring.Response = 0.28;
        _widthSpring.DampingFraction = 0.72;
        _heightSpring.Response = 0.28;
        _heightSpring.DampingFraction = 0.72;

        if (_dock == NotchDock.Top)
        {
            double targetW = hasMusic ? NotchConstants.AudioPeekWidth : NotchConstants.PeekWidth;
            double targetH = hasMusic ? NotchConstants.AudioPeekHeight : NotchConstants.PeekHeight;
            _widthSpring.AnimateTo(targetW, _currentWidth);
            _heightSpring.AnimateTo(targetH, _currentHeight);
        }
        else
        {
            _widthSpring.AnimateTo(NotchConstants.PeekHeight, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.PeekWidth, _currentHeight);
        }

        _topRadiusSpring.AnimateTo(NotchConstants.PeekTopRadius, _currentTopRadius);
        _bottomRadiusSpring.AnimateTo(NotchConstants.PeekBottomRadius, _currentBottomRadius);
        _shadowOpacitySpring.AnimateTo(ClosedShadowOpacity + 0.15, _currentShadowOpacity);
        _auraOpacitySpring.AnimateTo(hasMusic ? 0.38 : 0.0, _currentAuraOpacity);
    }

    private void TransitionToClose()
    {
        _cameraPopUpTimer?.Stop();
        _isCameraPopUpActive = false;
        LiveActivity.IsPeeking = false;

        if (_vm.NotchState == NotchState.Closed) return;
        NotifyPluginsStateChanged(NotchState.Closed);
        if (!_applyingSyncedState)
            UserStateChanged?.Invoke(NotchState.Closed);

        // Collapse settings & command center if open
        if (_isSettingsExpanded)
        {
            _isSettingsExpanded = false;
            InlineSettings.Visibility = Visibility.Collapsed;
            VerticalSettingsPanel.Visibility = Visibility.Collapsed;
        }
        if (_isCommandCenterExpanded)
        {
            _isCommandCenterExpanded = false;
            CommandCenterPanel.Visibility = Visibility.Collapsed;
        }

        _vm.Close();
        WindowHelper.DisableActivation(this);
        System.Windows.Input.Keyboard.ClearFocus();

        _widthSpring.Response = 0.34;
        _widthSpring.DampingFraction = 0.86;
        _heightSpring.Response = 0.30;
        _heightSpring.DampingFraction = 0.88;

        if (_dock == NotchDock.Top)
        {
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
        }
        else
        {
            // Side: narrow width, tall height
            _widthSpring.AnimateTo(NotchConstants.ClosedHeight, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedWidth, _currentHeight);
        }

        _topRadiusSpring.AnimateTo(NotchConstants.ClosedTopRadius, _currentTopRadius);
        _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        _shadowOpacitySpring.AnimateTo(ClosedShadowOpacity, _currentShadowOpacity);
        _contentOpacitySpring.AnimateTo(0.0, _currentContentOpacity);
        _contentScaleSpring.AnimateTo(0.92, _currentContentScale);
        _auraOpacitySpring.AnimateTo(0.0, _currentAuraOpacity);
    }

    #endregion

    #region Hover Logic

    private void OnNotchMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDragging || _isMouseDownOnNotch) return;
        _isMouseOverNotch = true;
        _hoverCloseTimer?.Stop();

        if (_vm.NotchState == NotchState.Closed)
        {
            _hoverOpenTimer?.Stop();

            if (_settings.HoverMode == HoverMode.HoverPeekClickOpen)
            {
                // Peek mode: slight expand on hover, click to fully open
                _hoverOpenTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(50, _settings.HoverOpenDelayMs))
                };
                _hoverOpenTimer.Tick += (_, _) =>
                {
                    _hoverOpenTimer!.Stop();
                    if (_isDragging || _isMouseDownOnNotch) return;
                    if (_isMouseOverNotch && _vm.NotchState == NotchState.Closed)
                        TransitionToPeek();
                };
                _hoverOpenTimer.Start();
            }
            else // LongHoverOpen
            {
                // Long hover: wait longer then fully open
                _hoverOpenTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(_settings.LongHoverDelayMs)
                };
                _hoverOpenTimer.Tick += (_, _) =>
                {
                    _hoverOpenTimer!.Stop();
                    if (_isDragging || _isMouseDownOnNotch) return;
                    if (_isMouseOverNotch)
                        TransitionToOpen();
                };
                _hoverOpenTimer.Start();
            }
        }
    }

    private void OnNotchMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;
        _isMouseOverNotch = false;
        _hoverOpenTimer?.Stop();

        // Don't close while a context menu is open or user is actively typing with keyboard focus within
        if (_isContextMenuOpen || IsKeyboardFocusWithin) return;

        if (_vm.NotchState == NotchState.Open || _vm.NotchState == NotchState.Peeking)
        {
            _hoverCloseTimer?.Stop();
            _hoverCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(
                    _vm.NotchState == NotchState.Peeking ? 80 : Math.Max(50, _settings.HoverCloseDelayMs))
            };
            _hoverCloseTimer.Tick += (_, _) =>
            {
                _hoverCloseTimer!.Stop();
                if (!_isMouseOverNotch && !_isContextMenuOpen && !IsKeyboardFocusWithin)
                    TransitionToClose();
            };
            _hoverCloseTimer.Start();
        }
    }

    private void OnNotchMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Double-click on closed notch toggles mute
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && _vm.NotchState != NotchState.Open)
        {
            _volumeService?.ToggleMute();
            e.Handled = true;
            return;
        }

        // Don't capture for drag when open — let buttons/controls work normally
        if (_vm.NotchState == NotchState.Open && sender != NotchPath) return;

        var cursorPos = System.Windows.Forms.Cursor.Position;
        _dragStartScreen = new Point(cursorPos.X, cursorPos.Y);
        _dragLastScreen = _dragStartScreen;
        _dragLastTime = DateTime.UtcNow;
        _dragVelocityX = 0;
        _dragVelocityY = 0;
        _isDragging = false;
        _isMouseDownOnNotch = true;

        // Suppress hover timers while mouse is down
        _hoverOpenTimer?.Stop();

        NotchPath.CaptureMouse();
    }

    private void OnNotchMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseDownOnNotch && !_isDragging) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

        // Use raw cursor position to avoid drift from window movement
        var cursorPos = System.Windows.Forms.Cursor.Position;
        var currentScreen = new Point(cursorPos.X, cursorPos.Y);

        // Check drag threshold
        if (_isMouseDownOnNotch && !_isDragging)
        {
            double dx = currentScreen.X - _dragStartScreen.X;
            double dy = currentScreen.Y - _dragStartScreen.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > NotchConstants.DragThreshold)
            {
                BeginDrag(currentScreen);
            }
            else return;
        }

        if (!_isDragging) return;

        // Track velocity (exponential smoothing with clamped time delta)
        var now = DateTime.UtcNow;
        double dt = (now - _dragLastTime).TotalSeconds;
        // Clamp dt to prevent huge velocity spikes from timer irregularities
        dt = Math.Clamp(dt, 0.001, 0.1);

        if (dt > 0.001)
        {
            double vx = (currentScreen.X - _dragLastScreen.X) / dt;
            double vy = (currentScreen.Y - _dragLastScreen.Y) / dt;
            _dragVelocityX = _dragVelocityX * 0.6 + vx * 0.4;
            _dragVelocityY = _dragVelocityY * 0.6 + vy * 0.4;
        }
        _dragLastScreen = currentScreen;
        _dragLastTime = now;

        // Move window in physical px, sized for the cursor's current screen DPI
        double dragScale = ScreenHelper.GetScaleForScreen(ScreenHelper.ScreenFromPhysicalPoint(
            new System.Drawing.Point((int)currentScreen.X, (int)currentScreen.Y)));
        int pillW = (int)Math.Round(NotchConstants.ClosedWidth * 0.8 * dragScale);
        int pillH = (int)Math.Round(NotchConstants.ClosedHeight * 0.8 * dragScale);
        WindowHelper.SetWindowRectPhysical(this,
            (int)currentScreen.X - pillW / 2,
            (int)currentScreen.Y - pillH / 2,
            pillW, pillH);
    }

    private void OnNotchMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isMouseDownOnNotch = false;

        // Always release mouse capture to prevent stuck states
        if (NotchPath.IsMouseCaptured)
            NotchPath.ReleaseMouseCapture();

        if (_isDragging)
        {
            EndDrag();
        }
        else
        {
            // Short click — normal behavior (no movement occurred)
            if (_vm.NotchState == NotchState.Closed || _vm.NotchState == NotchState.Peeking)
                TransitionToOpen();
        }
    }

    private void BeginDrag(Point currentScreenPos)
    {
        _isDragging = true;
        _isMouseDownOnNotch = false;

        // A floating island shouldn't hold reserved screen space
        _appBar.Release();

        // Target drag pill size
        double pillW = NotchConstants.ClosedWidth * 0.8;
        double pillH = NotchConstants.ClosedHeight * 0.8;

        // If coming from a side dock, immediately set dimensions to horizontal
        if (_dock == NotchDock.Left || _dock == NotchDock.Right)
        {
            _currentWidth = pillW;
            _currentHeight = pillH;
        }

        // Set window to drag pill size and center on cursor, in physical px
        double beginScale = ScreenHelper.GetScaleForScreen(ScreenHelper.ScreenFromPhysicalPoint(
            new System.Drawing.Point((int)currentScreenPos.X, (int)currentScreenPos.Y)));
        WindowHelper.SetWindowRectPhysical(this,
            (int)(currentScreenPos.X - pillW * beginScale / 2),
            (int)(currentScreenPos.Y - pillH * beginScale / 2),
            (int)Math.Round(pillW * beginScale),
            (int)Math.Round(pillH * beginScale));

        // Switch to horizontal content during drag
        ClosedContent.Visibility = Visibility.Visible;
        OpenContent.Visibility = Visibility.Visible;
        VerticalClosedContent.Visibility = Visibility.Collapsed;
        VerticalOpenContent.Visibility = Visibility.Collapsed;

        // Close the notch to a small dragging pill
        if (_vm.NotchState != NotchState.Closed)
        {
            _vm.Close();
            _isSettingsExpanded = false;
            _isCommandCenterExpanded = false;
            InlineSettings.Visibility = Visibility.Collapsed;
            CommandCenterPanel.Visibility = Visibility.Collapsed;
        }

        // Animate to compact drag pill size
        _widthSpring.Response = 0.25;
        _widthSpring.DampingFraction = 0.90;
        _heightSpring.Response = 0.25;
        _heightSpring.DampingFraction = 0.90;
        _widthSpring.AnimateTo(pillW, _currentWidth);
        _heightSpring.AnimateTo(pillH, _currentHeight);
        _shadowOpacitySpring.AnimateTo(DragShadowOpacity, _currentShadowOpacity);
        _contentOpacitySpring.AnimateTo(0.0, _currentContentOpacity);
    }

    private void EndDrag()
    {
        _isDragging = false;

        // Dock on whichever screen the cursor was released on
        var dropScreen = ScreenHelper.ScreenFromPhysicalPoint(
            new System.Drawing.Point((int)_dragLastScreen.X, (int)_dragLastScreen.Y));
        _screenDevice = dropScreen.DeviceName;
        var screenBounds = dropScreen.Bounds; // physical px
        double dropScale = ScreenHelper.GetScaleForScreen(dropScreen);

        double cursorX = _dragLastScreen.X;
        double cursorY = _dragLastScreen.Y;

        // Determine target dock based on position + velocity (thresholds are DIP
        // constants — scale them to this screen's physical pixels)
        NotchDock targetDock = NotchDock.Top;
        double edgeDist = NotchConstants.EdgeSnapDistance * dropScale;
        double throwV = NotchConstants.ThrowVelocityThreshold * dropScale;

        // Any edge of the cursor's screen is a valid dock target — including edges
        // facing another monitor (drag past the seam to dock on the far screen).
        bool nearLeft = cursorX - screenBounds.Left < edgeDist || _dragVelocityX < -throwV;
        bool nearRight = screenBounds.Right - cursorX < edgeDist || _dragVelocityX > throwV;
        bool nearTop = cursorY - screenBounds.Top < edgeDist * 1.5;

        if (nearLeft && !nearTop)
            targetDock = NotchDock.Left;
        else if (nearRight && !nearTop)
            targetDock = NotchDock.Right;
        else
            targetDock = NotchDock.Top;

        DockTo(targetDock);
    }

    private void DockTo(NotchDock dock)
    {
        _dock = dock;

        // Reset state so subsequent TransitionToOpen() isn't skipped
        _vm.Close();

        RepositionForDock();
        UpdateCameraProximityTimerState();

        // Persist where the main island lives so it survives restarts
        if (_isPrimary)
        {
            _settings.NotchDockPosition = dock;
            _settings.NotchScreenDevice = _screenDevice;
            _settings.Save();
            SettingsUpdatedByUser?.Invoke(_settings);
        }

        // Move (or drop) the reserved strip to the new screen/edge
        UpdateSpaceReservation();

        // Update HUD overlay active state based on dock
        HudOverlay.IsActive = (dock == NotchDock.Top);
        VerticalHudOverlay.IsActive = (dock != NotchDock.Top);

        // Switch content panels based on dock orientation
        if (_dock == NotchDock.Top)
        {
            ClosedContent.Visibility = Visibility.Visible;
            OpenContent.Visibility = Visibility.Visible;
            VerticalClosedContent.Visibility = Visibility.Collapsed;
            VerticalOpenContent.Visibility = Visibility.Collapsed;
        }
        else
        {
            ClosedContent.Visibility = Visibility.Collapsed;
            OpenContent.Visibility = Visibility.Collapsed;
            VerticalClosedContent.Visibility = Visibility.Visible;
            VerticalOpenContent.Visibility = Visibility.Visible;
        }

        InjectPluginUI();

        // Animate back to closed dimensions
        _widthSpring.Response = 0.40;
        _widthSpring.DampingFraction = 0.82;
        _heightSpring.Response = 0.40;
        _heightSpring.DampingFraction = 0.82;

        if (_dock == NotchDock.Top)
        {
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
        }
        else
        {
            // Side dock: swap width/height (tall & narrow)
            _widthSpring.AnimateTo(NotchConstants.ClosedHeight, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedWidth, _currentHeight);
        }

        _topRadiusSpring.AnimateTo(NotchConstants.ClosedTopRadius, _currentTopRadius);
        _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        _shadowOpacitySpring.AnimateTo(ClosedShadowOpacity, _currentShadowOpacity);
        _contentOpacitySpring.AnimateTo(0.0, _currentContentOpacity);
    }

    private void RepositionForDock()
    {
        RefreshScreenBounds();

        // Physical dimensions for THIS screen's DPI
        int longAxis = (int)Math.Round(_fixedWindowWidth * _screenScale);
        int shortAxis = (int)Math.Round(_fixedWindowHeight * _screenScale);
        int inset = (int)Math.Round(2 * _screenScale);

        switch (_dock)
        {
            case NotchDock.Top:
                WindowHelper.SetWindowRectPhysical(this,
                    _screenPhysical.Left + (_screenPhysical.Width - longAxis) / 2,
                    _screenPhysical.Top - inset,
                    longAxis, shortAxis);
                break;

            case NotchDock.Left:
                // Vertical window on the left edge
                WindowHelper.SetWindowRectPhysical(this,
                    _screenPhysical.Left - inset,
                    _screenPhysical.Top + (_screenPhysical.Height - longAxis) / 2,
                    shortAxis, longAxis);
                break;

            case NotchDock.Right:
                // Vertical window on the right edge
                WindowHelper.SetWindowRectPhysical(this,
                    _screenPhysical.Right - shortAxis + inset,
                    _screenPhysical.Top + (_screenPhysical.Height - longAxis) / 2,
                    shortAxis, longAxis);
                break;
        }
    }

    private void OnContentGridMouseMove(object sender, MouseEventArgs e)
    {
        // Forward to drag handler
        OnNotchMouseMove(sender, e);
    }

    private void OnContentGridMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Handle drag end or click-to-open
        OnNotchMouseUp(sender, e);
    }

    private void OnRootGridMouseLeave(object sender, MouseEventArgs e)
    {
        // If dragging and mouse leaves window (very fast movement), end drag safely
        if (_isDragging && e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            _isMouseDownOnNotch = false;
            if (NotchPath.IsMouseCaptured)
                NotchPath.ReleaseMouseCapture();
            EndDrag();
        }
    }

    private void OnNotchMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // Only adjust volume in closed/peeking state (open state scrolls content normally)
        if (_vm.NotchState == NotchState.Open) return;

        // ShowVolumeHud only gates the HUD display (inside HudOverlay), not the volume change itself
        if (_volumeService != null)
        {
            float current = _volumeService.Volume;
            // e.Delta is typically 120 per notch; scale to ~5% per scroll tick
            float delta = (e.Delta / 120f) * 0.05f;
            _volumeService.SetVolume(Math.Clamp(current + delta, 0f, 1f));
            e.Handled = true;
        }
    }

    private void OnNotchRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = CreateStyledContextMenu();

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        TrackContextMenu(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnGlobalHotkeyPressed()
    {
        Dispatcher.Invoke(() =>
        {
            if (_vm.NotchState == NotchState.Open)
                TransitionToClose();
            else
                TransitionToOpen();
        });
    }

    /// <summary>
    /// Tracks a context menu so the notch doesn't close while it's open.
    /// </summary>
    public void TrackContextMenu(System.Windows.Controls.ContextMenu menu)
    {
        _isContextMenuOpen = true;
        menu.Closed += (_, _) =>
        {
            _isContextMenuOpen = false;
            // Re-check if mouse has left while menu was open
            if (!_isMouseOverNotch && _vm.NotchState == NotchState.Open)
            {
                _hoverCloseTimer?.Stop();
                _hoverCloseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(50, _settings.HoverCloseDelayMs))
                };
                _hoverCloseTimer.Tick += (_, _) =>
                {
                    _hoverCloseTimer!.Stop();
                    if (!_isMouseOverNotch && !_isContextMenuOpen)
                        TransitionToClose();
                };
                _hoverCloseTimer.Start();
            }
        };
    }

    /// <summary>
    /// Creates a styled ContextMenu matching the app's dark/light theme with rounded corners.
    /// </summary>
    public System.Windows.Controls.ContextMenu CreateStyledContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // Create a border template with rounded corners
        var template = new ControlTemplate(typeof(System.Windows.Controls.ContextMenu));
        var frameworkFactory = new FrameworkElementFactory(typeof(Border));
        frameworkFactory.SetValue(Border.BackgroundProperty, new DynamicResourceExtension("NotchFillBrush"));
        frameworkFactory.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("SeparatorBrush"));
        frameworkFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frameworkFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        frameworkFactory.SetValue(Border.PaddingProperty, new Thickness(6, 8, 6, 8));

        // Add drop shadow effect
        var shadowEffect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = System.Windows.Media.Colors.Black,
            Direction = 270,
            ShadowDepth = 2,
            BlurRadius = 12,
            Opacity = 0.3
        };
        frameworkFactory.SetValue(Border.EffectProperty, shadowEffect);

        var scrollFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.ScrollViewer));
        scrollFactory.SetValue(System.Windows.Controls.ScrollViewer.CanContentScrollProperty, true);
        scrollFactory.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
            System.Windows.Controls.ScrollBarVisibility.Disabled);
        scrollFactory.SetValue(System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty,
            System.Windows.Controls.ScrollBarVisibility.Auto);

        var stackFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.StackPanel));
        stackFactory.SetValue(System.Windows.Controls.StackPanel.IsItemsHostProperty, true);

        scrollFactory.AppendChild(stackFactory);
        frameworkFactory.AppendChild(scrollFactory);
        template.VisualTree = frameworkFactory;
        menu.Template = template;

        // Style for MenuItems with rounded hover
        var menuItemStyle = new Style(typeof(System.Windows.Controls.MenuItem));
        menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.ForegroundProperty,
            FindResource("TextSecondaryBrush")));
        menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.FontSizeProperty, 12.0));
        menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.PaddingProperty,
            new Thickness(12, 6, 24, 6)));
        menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.MarginProperty,
            new Thickness(2, 1, 2, 1)));
        menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.CursorProperty,
            System.Windows.Input.Cursors.Hand));

        // Create template for menu item with rounded background
        var itemTemplate = new ControlTemplate(typeof(System.Windows.Controls.MenuItem));
        var itemBorder = new FrameworkElementFactory(typeof(Border));
        itemBorder.Name = "ItemBorder";
        itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        itemBorder.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);

        var contentPresenter = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        contentPresenter.SetValue(System.Windows.Controls.ContentPresenter.ContentSourceProperty, "Header");
        contentPresenter.SetValue(System.Windows.Controls.ContentPresenter.MarginProperty, new Thickness(0));

        itemBorder.AppendChild(contentPresenter);
        itemTemplate.VisualTree = itemBorder;

        // Hover trigger with rounded background
        var hoverTrigger = new Trigger { Property = System.Windows.Controls.MenuItem.IsHighlightedProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.MenuItem.BackgroundProperty,
            FindResource("HoverOverlayBrush"), "ItemBorder"));
        hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.MenuItem.ForegroundProperty,
            FindResource("TextPrimaryBrush")));
        itemTemplate.Triggers.Add(hoverTrigger);

        menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.TemplateProperty, itemTemplate));
        menu.Resources.Add(typeof(System.Windows.Controls.MenuItem), menuItemStyle);

        // Style for Separator
        var separatorStyle = new Style(typeof(System.Windows.Controls.Separator));
        separatorStyle.Setters.Add(new Setter(System.Windows.Controls.Separator.BackgroundProperty,
            FindResource("SeparatorBrush")));
        separatorStyle.Setters.Add(new Setter(System.Windows.Controls.Separator.MarginProperty,
            new Thickness(8, 4, 8, 4)));
        separatorStyle.Setters.Add(new Setter(System.Windows.Controls.Separator.HeightProperty, 1.0));
        menu.Resources.Add(typeof(System.Windows.Controls.Separator), separatorStyle);

        return menu;
    }

    #endregion

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _cachedDpiX = newDpi.DpiScaleX;
        _cachedDpiY = newDpi.DpiScaleY;
        _dpiCached = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Shared services are owned and disposed by App — a secondary island
        // closing (e.g. multi-screen toggled off) must not tear them down.
        CompositionTarget.Rendering -= OnRenderBackdrop;
        CompositionTarget.Rendering -= OnRendering;
        _hoverOpenTimer?.Stop();
        _hoverCloseTimer?.Stop();
        _cameraProximityTimer?.Stop();
        _idleTrimTimer?.Stop();
        StopwatchWidget?.Dispose();
        VitalsWidget?.Dispose();
        _appBar.Dispose();
        _pluginManagerWindow?.Close();
        _pluginManagerWindow = null;
        _pluginManager?.Dispose();
        _globalHotkey?.Dispose();
        base.OnClosed(e);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        UpdateCameraProximityTimerState();

        // Apply liquid glass theme if toggled
        _themeService.Apply(settings);
        ApplyLiquidGlassEffect(settings.Theme == AppTheme.LiquidGlass || settings.UseLiquidGlassTheme);

        // Toggle visibility of components based on settings
        LiveActivity.UserEnabled = settings.ShowMusicControls;
        BatteryIndicator.UserEnabled = settings.ShowBattery;
        CalendarPanel.Visibility = settings.ShowCalendar ? Visibility.Visible : Visibility.Collapsed;
        CompactVisualizer_SetVisible(settings.ShowVisualizer);
        WaterWidget.Bind(settings);
        SleepWidget.Bind(settings);
        UpdateCarouselCards();
        UpdateClosedContentVisibility();

        // HUD gates (external volume/brightness changes must respect these too)
        HudOverlay.VolumeHudEnabled = settings.ShowVolumeHud;
        HudOverlay.BrightnessHudEnabled = settings.ShowBrightnessHud;
        VerticalHudOverlay.VolumeHudEnabled = settings.ShowVolumeHud;
        VerticalHudOverlay.BrightnessHudEnabled = settings.ShowBrightnessHud;

        // Webcam
        _webcamService.SetTargetFps(settings.WebcamFps);
        if (settings.ShowWebcam)
        {
            WebcamPanel.Visibility = Visibility.Visible;
            WebcamMirror.Bind(_webcamService);
            if (_webcamService.MaxCameraFps > 0)
            {
                InlineSettings.UpdateWebcamFpsMax(_webcamService.MaxCameraFps);
                VerticalInlineSettings.UpdateWebcamFpsMax(_webcamService.MaxCameraFps);
            }
        }
        else
        {
            WebcamPanel.Visibility = Visibility.Collapsed;
            WebcamMirror.StopCamera();
        }

        // Re-adjust notch size if currently open
        if (_vm.NotchState == NotchState.Open)
        {
            if (_dock == NotchDock.Top)
            {
                _widthSpring.AnimateTo(GetOpenWidth(), _currentWidth);

                if (!_isSettingsExpanded)
                {
                    double targetH = GetTopOpenHeight();
                    _heightSpring.AnimateTo(targetH, _currentHeight);
                }
            }
            // Side docks: don't re-animate width/height — settings panel has fixed size
        }

        UpdateSpaceReservation();

        // Re-target the shadow for the current state (ShowShadow may have toggled)
        if (!_isDragging)
        {
            _shadowOpacitySpring.AnimateTo(
                _vm.NotchState == NotchState.Open ? OpenShadowOpacity : ClosedShadowOpacity,
                _currentShadowOpacity);
        }
    }

    private double GetOpenWidth()
    {
        double accessoryWidth = GetMeasuredPanelWidth(PluginAccessoryPanel, NotchConstants.OpenHeight);
        bool cal = _settings.ShowCalendar;
        bool cam = _settings.ShowWebcam;

        double baseWidth = NotchConstants.OpenWidth;
        if (cal && cam)
            baseWidth = NotchConstants.OpenWidthWithCalendarAndWebcam;
        else if (cal)
            baseWidth = NotchConstants.OpenWidthWithCalendar;
        else if (cam)
            baseWidth = NotchConstants.OpenWidthWithWebcam;

        if (_isCommandCenterExpanded)
            baseWidth = Math.Max(baseWidth, 580);

        return baseWidth + accessoryWidth;
    }

    #region Open Notch Widget Carousel

    private int _carouselIndex;
    private readonly List<(FrameworkElement Element, string Title)> _enabledCards = [];

    private void UpdateCarouselCards()
    {
        _enabledCards.Clear();

        if (_settings.ShowMusicControls && _settings.ShowMusicCard)
            _enabledCards.Add((MusicPlayer, "🎵 Music"));
        if (_settings.ShowStopwatchCard)
            _enabledCards.Add((StopwatchWidget, "⏱ Stopwatch"));
        if (_settings.ShowWaterCard)
            _enabledCards.Add((WaterWidget, "💧 Water"));
        if (_settings.ShowSleepCard)
            _enabledCards.Add((SleepWidget, "🌙 Sleep"));
        if (_settings.ShowTasksCard)
            _enabledCards.Add((TasksWidget, "✓ Tasks"));
        if (_settings.ShowVitalsCard)
            _enabledCards.Add((VitalsWidget, "⚡ Vitals"));
        if (_settings.ShowNotificationsCard)
            _enabledCards.Add((NotificationHistoryWidget, "🔔 Alerts"));

        // If none enabled, fallback to MusicPlayer
        if (_enabledCards.Count == 0)
        {
            _enabledCards.Add((MusicPlayer, "🎵 Music"));
        }

        _carouselIndex = Math.Clamp(_carouselIndex, 0, _enabledCards.Count - 1);

        bool multi = _enabledCards.Count > 1;
        CarouselPrevArrow.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        CarouselNextArrow.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;

        ShowActiveCard();
    }

    private static (TranslateTransform Translate, ScaleTransform Scale) GetCardTransforms(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group &&
            group.Children.Count >= 2 &&
            group.Children[0] is TranslateTransform translate &&
            group.Children[1] is ScaleTransform scale)
        {
            return (translate, scale);
        }

        var newTranslate = new TranslateTransform(0, 0);
        var newScale = new ScaleTransform(1, 1);
        var newGroup = new TransformGroup();
        newGroup.Children.Add(newTranslate);
        newGroup.Children.Add(newScale);
        element.RenderTransform = newGroup;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (newTranslate, newScale);
    }

    private void ShowActiveCard()
    {
        // Cancel all animations and collapse every card
        for (int i = 0; i < _enabledCards.Count; i++)
        {
            var elem = _enabledCards[i].Element;
            elem.Visibility = Visibility.Collapsed;
            elem.BeginAnimation(UIElement.OpacityProperty, null);
            var (translate, scale) = GetCardTransforms(elem);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translate.X = 0;
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
            elem.Opacity = 1.0;
        }

        if (_carouselIndex >= 0 && _carouselIndex < _enabledCards.Count)
        {
            var active = _enabledCards[_carouselIndex].Element;
            active.Visibility = Visibility.Visible;
            active.Opacity = 1.0;
        }

        UpdateCarouselDots();
    }

    private void UpdateCarouselDots()
    {
        if (CarouselDotsPanel == null) return;
        CarouselDotsPanel.Children.Clear();

        if (_enabledCards.Count <= 1)
        {
            CarouselDotsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        CarouselDotsPanel.Visibility = Visibility.Visible;
        for (int i = 0; i < _enabledCards.Count; i++)
        {
            int index = i;
            bool isActive = (i == _carouselIndex);
            var dot = new Border
            {
                Width = isActive ? 14 : 5,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = _enabledCards[i].Title
            };
            dot.MouseLeftButtonDown += (_, _) =>
            {
                if (_carouselIndex != index)
                {
                    int oldIdx = _carouselIndex;
                    int dir = index > oldIdx ? 1 : -1;
                    _carouselIndex = index;
                    TransitionToCard(oldIdx, index, dir);
                }
            };
            CarouselDotsPanel.Children.Add(dot);
        }
    }

    private int _carouselTransitionId;
    private DateTime _lastCarouselNavTime = DateTime.MinValue;

    private void TransitionToCard(int oldIdx, int newIdx, int direction)
    {
        if (oldIdx == newIdx || oldIdx < 0 || oldIdx >= _enabledCards.Count || newIdx < 0 || newIdx >= _enabledCards.Count)
        {
            _carouselIndex = newIdx;
            ShowActiveCard();
            return;
        }

        int transitionId = ++_carouselTransitionId;

        // 1. Immediately cancel animations and collapse all other cards to prevent any overlaying
        for (int i = 0; i < _enabledCards.Count; i++)
        {
            if (i != oldIdx && i != newIdx)
            {
                var other = _enabledCards[i].Element;
                other.Visibility = Visibility.Collapsed;
                other.BeginAnimation(UIElement.OpacityProperty, null);
                var (tt, st) = GetCardTransforms(other);
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                tt.X = 0;
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
            }
        }

        var outgoing = _enabledCards[oldIdx].Element;
        var incoming = _enabledCards[newIdx].Element;
        _carouselIndex = newIdx;

        // Reset previous animations on outgoing and incoming
        outgoing.BeginAnimation(UIElement.OpacityProperty, null);
        incoming.BeginAnimation(UIElement.OpacityProperty, null);

        var (outTranslate, outScale) = GetCardTransforms(outgoing);
        outTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        outScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        outScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        var (inTranslate, inScale) = GetCardTransforms(incoming);
        inTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        inScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        inScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        double outTargetX = direction > 0 ? -28 : 28;
        double inStartX = direction > 0 ? 28 : -28;

        // Animate outgoing card
        outgoing.Visibility = Visibility.Visible;
        outgoing.Opacity = 1.0;
        outTranslate.X = 0;
        outScale.ScaleX = 1.0;
        outScale.ScaleY = 1.0;

        var outAnimX = new DoubleAnimation(0, outTargetX, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        var outAnimOpacity = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var outAnimScale = new DoubleAnimation(1.0, 0.95, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };

        outAnimOpacity.Completed += (_, _) =>
        {
            if (transitionId != _carouselTransitionId) return; // Drop stale callbacks
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.BeginAnimation(UIElement.OpacityProperty, null);
            outTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            outScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            outScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            outTranslate.X = 0;
            outScale.ScaleX = 1.0;
            outScale.ScaleY = 1.0;
            outgoing.Opacity = 1.0;
        };

        outTranslate.BeginAnimation(TranslateTransform.XProperty, outAnimX);
        outScale.BeginAnimation(ScaleTransform.ScaleXProperty, outAnimScale);
        outScale.BeginAnimation(ScaleTransform.ScaleYProperty, outAnimScale);
        outgoing.BeginAnimation(UIElement.OpacityProperty, outAnimOpacity);

        // Prepare incoming card
        incoming.Visibility = Visibility.Visible;
        inTranslate.X = inStartX;
        inScale.ScaleX = 0.95;
        inScale.ScaleY = 0.95;
        incoming.Opacity = 0.0;

        var inAnimX = new DoubleAnimation(inStartX, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        var inAnimOpacity = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var inAnimScale = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };

        inAnimOpacity.Completed += (_, _) =>
        {
            if (transitionId != _carouselTransitionId) return; // Drop stale callbacks
            ShowActiveCard();
        };

        inTranslate.BeginAnimation(TranslateTransform.XProperty, inAnimX);
        inScale.BeginAnimation(ScaleTransform.ScaleXProperty, inAnimScale);
        inScale.BeginAnimation(ScaleTransform.ScaleYProperty, inAnimScale);
        incoming.BeginAnimation(UIElement.OpacityProperty, inAnimOpacity);

        // Failsafe watchdog: after 240ms, ensure active card is guaranteed settled, visible and opaque
        var watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        watchdogTimer.Tick += (_, _) =>
        {
            watchdogTimer.Stop();
            if (transitionId == _carouselTransitionId)
            {
                ShowActiveCard();
            }
        };
        watchdogTimer.Start();
    }

    private void NavigateCarousel(int delta)
    {
        if (_enabledCards.Count <= 1) return;

        // Debounce rapid input to prevent animation collisions and blank states
        var now = DateTime.UtcNow;
        if ((now - _lastCarouselNavTime).TotalMilliseconds < 120)
        {
            return;
        }
        _lastCarouselNavTime = now;

        int oldIdx = _carouselIndex;
        int newIdx = (_carouselIndex + delta + _enabledCards.Count) % _enabledCards.Count;
        TransitionToCard(oldIdx, newIdx, delta);
    }

    private void OnCarouselPrevClick(object sender, RoutedEventArgs e)
    {
        NavigateCarousel(-1);
    }

    private void OnCarouselNextClick(object sender, RoutedEventArgs e)
    {
        NavigateCarousel(1);
    }

    private void OnCarouselArrowMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_enabledCards.Count <= 1) return;
        if (e.Delta < 0) NavigateCarousel(1);
        else if (e.Delta > 0) NavigateCarousel(-1);
        e.Handled = true;
    }

    private void OnCarouselMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_enabledCards.Count <= 1) return;
        // Support horizontal mouse wheel scrolling (Shift + Wheel) for switching cards without hijacking vertical scroll
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
        {
            if (e.Delta < 0) NavigateCarousel(1);
            else if (e.Delta > 0) NavigateCarousel(-1);
            e.Handled = true;
        }
    }

    #endregion

    private void ApplyLiquidGlassEffect(bool enable)
    {
        _isLiquidGlassActive = enable;

        if (enable)
        {
            WindowHelper.SetExcludeFromCapture(this, true);

            // Cache DPI once
            if (!_dpiCached)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                _cachedDpiX = dpi.DpiScaleX;
                _cachedDpiY = dpi.DpiScaleY;
                _dpiCached = true;
            }

            GlassSpecularPath.Opacity = 0.0;
            GlassSpecularPath.Visibility = Visibility.Collapsed;
            GlassBorderPath.Opacity = 0.0;
            GlassBorderPath.Visibility = Visibility.Collapsed;
            GlassInnerShadowPath.Opacity = 0.0;
            GlassInnerShadowPath.Visibility = Visibility.Collapsed;
            BackdropBlurPath.Opacity = 1.0;

            // Idempotent: remove first so repeated enables never double-subscribe
            CompositionTarget.Rendering -= OnRenderBackdrop;
            CompositionTarget.Rendering += OnRenderBackdrop;
        }
        else
        {
            CompositionTarget.Rendering -= OnRenderBackdrop;
            WindowHelper.SetExcludeFromCapture(this, false);
            GlassSpecularPath.Opacity = 0;
            GlassBorderPath.Opacity = 0;
            GlassInnerShadowPath.Opacity = 0;
            BackdropBlurPath.Opacity = 0;
            BackdropBlurPath.Fill = null;
            _backdropBrush = null;
            _backdropWb = null;
        }
    }

    private long _lastBackdropCaptureTicks;

    private void OnRenderBackdrop(object? sender, EventArgs e)
    {
        if (!_isLiquidGlassActive || _isDragging) return;
        if (_currentWidth < 10 || _currentHeight < 10) return;

        // 30fps is indistinguishable under the blur and halves the capture cost
        long now = Environment.TickCount64;
        if (now - _lastBackdropCaptureTicks < 33) return;
        _lastBackdropCaptureTicks = now;

        try
        {
            double canvasLeft = System.Windows.Controls.Canvas.GetLeft(NotchPath);
            double canvasTop = System.Windows.Controls.Canvas.GetTop(NotchPath);
            if (double.IsNaN(canvasLeft)) canvasLeft = 0;
            if (double.IsNaN(canvasTop)) canvasTop = 0;

            var winRect = WindowHelper.GetWindowRectPhysical(this);
            int px = winRect.Left + (int)(canvasLeft * _cachedDpiX);
            int py = winRect.Top + (int)(canvasTop * _cachedDpiY);
            int pw = (int)(_currentWidth * _cachedDpiX);
            int ph = (int)(_currentHeight * _cachedDpiY);

            if (pw < 4 || ph < 4) return;

            // Ensure WriteableBitmap matches current size
            if (_backdropWb == null || _wbW != pw || _wbH != ph)
            {
                _backdropWb = new System.Windows.Media.Imaging.WriteableBitmap(
                    pw, ph, 96, 96, PixelFormats.Bgra32, null);
                _wbW = pw;
                _wbH = ph;

                _backdropBrush = new ImageBrush(_backdropWb)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None
                };
                BackdropBlurPath.Fill = _backdropBrush;
            }

            // Capture directly into the WriteableBitmap (zero alloc)
            // Padding feeds the edge-lens: rim pixels sample from this outside band
            BackdropBlurHelper.CaptureInto(_backdropWb, px, py, pw, ph, padding: 8);
        }
        catch
        {
            // Silently ignore capture failures
        }
    }

    private void CompactVisualizer_SetVisible(bool visible)
    {
        // The visualizer is inside LiveActivity; toggle via its property
        if (LiveActivity.FindName("CompactVisualizer") is UIElement viz)
            viz.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    #region File Shelf / Drag-Drop

    private void UpdateShelfVisibility()
    {
        bool hasItems = _shelfService.HasItems;
        ShelfPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        // Adjust open height if notch is open
        if (_vm.NotchState == NotchState.Open && !_isSettingsExpanded)
        {
            double targetHeight = GetTopOpenHeight();
            _heightSpring.AnimateTo(targetHeight, _currentHeight);
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        // The file shelf lives in the horizontal (top dock) layout only
        if (_dock != NotchDock.Top) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;

            // Open the notch if closed/peeking to show drop zone
            if (_vm.NotchState != NotchState.Open)
                TransitionToOpen();

            // Show drop zone and expand for shelf
            ShelfPanel.Visibility = Visibility.Visible;
            ShelfPanel.ShowDropZone();

            if (!_isSettingsExpanded)
            {
                _heightSpring.AnimateTo(GetTopOpenHeight(), _currentHeight);
            }

            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_dock != NotchDock.Top) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        ShelfPanel.HideDropZone();

        // If no items, collapse shelf and shrink back
        if (!_shelfService.HasItems)
        {
            ShelfPanel.Visibility = Visibility.Collapsed;
            if (_vm.NotchState == NotchState.Open && !_isSettingsExpanded)
            {
                _heightSpring.AnimateTo(GetTopOpenHeight(), _currentHeight);
            }
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_dock != NotchDock.Top) return;

        ShelfPanel.HideDropZone();

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                _shelfService.AddFiles(files);
            }
            e.Handled = true;
        }
    }

    #endregion

    #region Spotlight Handler

    public void OpenSpotlight()
    {
        _spotlightLauncher?.Open();
    }

    #endregion

    #region Coding Agent & Notification Handlers

    private void OnShelfBadgeClick(object sender, RoutedEventArgs e)
    {
        TransitionToOpen();
    }

    public void ShowNotification(NotificationItem item)
    {
        _activeNotification = item;

        // Resolve authentic iOS Dynamic Island app-specific visuals, gradient badge & color
        var visual = NotificationVisualResolver.Resolve(item);
        if (visual.AppLogoSource != null)
        {
            NotificationAppIconImage.Source = visual.AppLogoSource;
            NotificationAppIconImage.Visibility = Visibility.Visible;
            NotificationIconText.Visibility = Visibility.Collapsed;
            NotificationIconBorder.Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            NotificationIconBorder.BorderBrush = Brushes.Transparent;
            NotificationIconBorder.BorderThickness = new Thickness(0);
        }
        else
        {
            NotificationAppIconImage.Source = null;
            NotificationAppIconImage.Visibility = Visibility.Collapsed;
            NotificationIconText.Visibility = Visibility.Visible;
            NotificationIconText.Text = visual.Glyph;
            NotificationIconText.Foreground = visual.ForegroundBrush;
            NotificationIconBorder.Background = visual.BackgroundBrush;
            NotificationIconBorder.BorderBrush = Brushes.Transparent;
            NotificationIconBorder.BorderThickness = new Thickness(0);
        }
        NotificationIconShadow.Color = visual.AccentColor;

        NotificationTitleText.Text = visual.Title;
        NotificationBodyText.Text = visual.Body;

        // Keep notch border completely clean and borderless during notification pop-ups
        _auraOpacitySpring.AnimateTo(0.0, _currentAuraOpacity);
        AmbientAuraPath.Opacity = 0.0;

        NotificationCapsule.Visibility = Visibility.Visible;
        NotificationCapsule.IsHitTestVisible = true;

        if (_vm.NotchState != NotchState.Open)
        {
            // Apple Dynamic Island interactive spring expansion with natural bounce
            _widthSpring.SetPhysics(0.36, 0.72);
            _heightSpring.SetPhysics(0.34, 0.76);
            _topRadiusSpring.SetPhysics(0.36, 0.82);
            _bottomRadiusSpring.SetPhysics(0.36, 0.82);

            _widthSpring.AnimateTo(350, _currentWidth);
            _heightSpring.AnimateTo(44, _currentHeight);
            _bottomRadiusSpring.AnimateTo(22, _currentBottomRadius);
        }

        // Staggered zoom-and-bloom entrance spring physics
        _notificationScaleSpring.AnimateTo(1.0, 0.86);
        _notificationTranslateSpring.AnimateTo(0.0, -4.0);
        _notificationIconScaleSpring.AnimateTo(1.0, 0.55);
        _notificationOpacitySpring.AnimateTo(1.0, 0.0);

        UpdateNotchVisuals();

        _notificationDismissTimer?.Stop();
        _notificationDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _notificationDismissTimer.Tick += (_, _) =>
        {
            _notificationDismissTimer.Stop();
            DismissNotificationCapsule();
        };
        _notificationDismissTimer.Start();
    }

    public void DismissNotificationCapsule()
    {
        _notificationDismissTimer?.Stop();

        // Graceful exit spring animation
        _notificationScaleSpring.AnimateTo(0.88, _currentNotificationScale);
        _notificationTranslateSpring.AnimateTo(-3.0, _currentNotificationTranslate);
        _notificationOpacitySpring.AnimateTo(0.0, _currentNotificationOpacity);
        _auraOpacitySpring.AnimateTo(0.0, _currentAuraOpacity);

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.32, 0.85);
            _heightSpring.SetPhysics(0.30, 0.85);
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
            _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        }

        var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        exitTimer.Tick += (_, _) =>
        {
            exitTimer.Stop();
            if (_activeNotification != null && _currentNotificationOpacity < 0.1)
            {
                NotificationCapsule.Visibility = Visibility.Collapsed;
                NotificationCapsule.IsHitTestVisible = false;
                _activeNotification = null;
                        UpdateClosedContentVisibility();
                UpdateNotchVisuals();
            }
        };
        exitTimer.Start();

        UpdateNotchVisuals();
    }

    private void OnDismissNotificationClick(object sender, RoutedEventArgs e)
    {
        DismissNotificationCapsule();
        e.Handled = true;
    }

    private void OnNotificationCapsuleClick(object sender, MouseButtonEventArgs e)
    {
        var item = _activeNotification;
        DismissNotificationCapsule();
        if (item != null)
        {
            if (item.AppName == "X-Notch")
            {
                TransitionToOpen();
                ExpandSettings();
            }
            else
            {
                AppLauncherHelper.LaunchApplicationForNotification(item);
            }
        }
        else
        {
            TransitionToOpen();
            NavigateToCard(NotificationHistoryWidget);
        }
        e.Handled = true;
    }

    private void OnOperatingModeCapsuleClick(object sender, MouseButtonEventArgs e)
    {
        DismissOperatingModeAlert();
        AppLauncherHelper.LaunchPowerApplication();
        TransitionToOpen();
        NavigateToCard(VitalsWidget);
        e.Handled = true;
    }

    public void NavigateToCard(FrameworkElement targetElement)
    {
        for (int i = 0; i < _enabledCards.Count; i++)
        {
            if (_enabledCards[i].Element == targetElement)
            {
                if (_carouselIndex != i)
                {
                    int oldIdx = _carouselIndex;
                    TransitionToCard(oldIdx, i, i > oldIdx ? 1 : -1);
                }
                break;
            }
        }
    }

    public void ShowApprovalPrompt(ApprovalRequest request)
    {
        _activeApproval = request;
        AgentPromptTitle.Text = $"{request.AgentId} — {request.ToolName}";
        AgentPromptCommand.Text = request.Command;

        // Keep notch border completely clean and borderless during alert pop-ups
        _auraOpacitySpring.AnimateTo(0.0, _currentAuraOpacity);
        AmbientAuraPath.Opacity = 0.0;

        AgentApprovalCapsule.Visibility = Visibility.Visible;
        AgentApprovalCapsule.IsHitTestVisible = true;

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.36, 0.72);
            _heightSpring.SetPhysics(0.34, 0.76);
            _topRadiusSpring.SetPhysics(0.36, 0.82);
            _bottomRadiusSpring.SetPhysics(0.36, 0.82);

            _widthSpring.AnimateTo(385, _currentWidth);
            _heightSpring.AnimateTo(44, _currentHeight);
            _bottomRadiusSpring.AnimateTo(22, _currentBottomRadius);
        }

        // Zoom-and-bloom spring physics
        _approvalScaleSpring.AnimateTo(1.0, 0.86);
        _approvalTranslateSpring.AnimateTo(0.0, -4.0);
        _approvalIconScaleSpring.AnimateTo(1.0, 0.55);
        _approvalOpacitySpring.AnimateTo(1.0, 0.0);

        UpdateNotchVisuals();
        Focus();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (_activeApproval != null)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                OnApproveAgentClick(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                OnDenyAgentClick(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }
    }

    private void DismissApprovalPrompt()
    {
        // Graceful exit spring animation
        _approvalScaleSpring.AnimateTo(0.88, _currentApprovalScale);
        _approvalTranslateSpring.AnimateTo(-3.0, _currentApprovalTranslate);
        _approvalOpacitySpring.AnimateTo(0.0, _currentApprovalOpacity);
        _auraOpacitySpring.AnimateTo(0.0, _currentAuraOpacity);

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.32, 0.85);
            _heightSpring.SetPhysics(0.30, 0.85);
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
            _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        }

        var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        exitTimer.Tick += (_, _) =>
        {
            exitTimer.Stop();
            if (_activeApproval != null && _currentApprovalOpacity < 0.1)
            {
                AgentApprovalCapsule.Visibility = Visibility.Collapsed;
                AgentApprovalCapsule.IsHitTestVisible = false;
                _activeApproval = null;
                        UpdateClosedContentVisibility();
                UpdateNotchVisuals();
            }
        };
        exitTimer.Start();

        UpdateNotchVisuals();
    }

    private void OnApproveAgentClick(object sender, RoutedEventArgs e)
    {
        if (_activeApproval != null)
        {
            _services.Approvals.SubmitDecision(_activeApproval.PromptId, ApprovalDecision.Allow);
        }
        DismissApprovalPrompt();
    }

    private void OnDenyAgentClick(object sender, RoutedEventArgs e)
    {
        if (_activeApproval != null)
        {
            _services.Approvals.SubmitDecision(_activeApproval.PromptId, ApprovalDecision.Deny);
        }
        DismissApprovalPrompt();
    }

    #endregion

    #region Camera Proximity Pop-Up Feature

    private void OnCameraTriggerEnter(object sender, MouseEventArgs e)
    {
        if (_settings.CameraHoverPopUp && _dock == NotchDock.Top && _vm.NotchState != NotchState.Open && !_isCameraPopUpActive)
        {
            TriggerCameraPopUp();
        }
    }

    private void OnCameraTriggerMove(object sender, MouseEventArgs e)
    {
        if (_settings.CameraHoverPopUp && _dock == NotchDock.Top && _vm.NotchState != NotchState.Open && !_isCameraPopUpActive)
        {
            TriggerCameraPopUp();
        }
    }

    private void UpdateCameraProximityTimerState()
    {
        if (_cameraProximityTimer == null) return;

        bool shouldRun = _settings.CameraHoverPopUp && _dock == NotchDock.Top;
        if (shouldRun && !_cameraProximityTimer.IsEnabled)
        {
            _cameraProximityTimer.Start();
        }
        else if (!shouldRun && _cameraProximityTimer.IsEnabled)
        {
            _cameraProximityTimer.Stop();
        }
    }

    private void OnCameraProximityTick(object? sender, EventArgs e)
    {
        if (!_settings.CameraHoverPopUp || _dock != NotchDock.Top || _isDragging || _vm.NotchState == NotchState.Open)
            return;

        // Skip if a fullscreen app is active on this monitor
        if (_fullscreenService.IsFullscreen && _fullscreenService.FullscreenMonitor == ScreenHelper.GetMonitorHandle(this))
            return;

        var cursorPos = System.Windows.Forms.Cursor.Position;
        if (!_screenPhysical.Contains(cursorPos))
        {
            _hasExitedCameraZone = true;
            return;
        }

        // Under the camera zone: within 135px horizontally of screen center, and top 38px of screen
        bool inCameraZone = CameraBezelHelper.IsInCameraZone(cursorPos, _screenPhysical);

        if (inCameraZone)
        {
            if (_hasExitedCameraZone && !_isCameraPopUpActive)
            {
                _hasExitedCameraZone = false;
                TriggerCameraPopUp();
            }
        }
        else
        {
            // Cursor has left the camera zone, arming for next entry
            _hasExitedCameraZone = true;
        }
    }

    public void PopUpAtCursor()
    {
        TriggerCameraPopUp();
    }

    public void TriggerCameraPopUp()
    {
        if (_vm.NotchState == NotchState.Open || _isDragging || _isMouseDownOnNotch) return;

        _isCameraPopUpActive = true;
        _hoverCloseTimer?.Stop();

        // Fluid pop-up animation: expand into glance dimensions with bouncy spring
        bool hasMusic = _settings.ShowMusicControls && _mediaService.MediaInfo.HasMedia && _mediaService.MediaInfo.IsPlaying;
        double targetW = hasMusic ? NotchConstants.AudioPeekWidth : Math.Max(240, NotchConstants.PeekWidth);
        double targetH = hasMusic ? NotchConstants.AudioPeekHeight : 38;

        _widthSpring.Response = 0.32;
        _widthSpring.DampingFraction = 0.74;
        _heightSpring.Response = 0.28;
        _heightSpring.DampingFraction = 0.68;

        _widthSpring.AnimateTo(targetW, _currentWidth);
        _heightSpring.AnimateTo(targetH, _currentHeight);
        _topRadiusSpring.AnimateTo(NotchConstants.PeekTopRadius, _currentTopRadius);
        _bottomRadiusSpring.AnimateTo(NotchConstants.PeekBottomRadius, _currentBottomRadius);
        _shadowOpacitySpring.AnimateTo(ClosedShadowOpacity + 0.20, _currentShadowOpacity);

        if (hasMusic)
            LiveActivity.IsPeeking = true;

        // Auto-dismiss pop-up after 1-2 seconds (default 1800ms)
        _cameraPopUpTimer?.Stop();
        int durationMs = _settings.CameraPopUpDurationMs > 0 ? _settings.CameraPopUpDurationMs : 1800;
        _cameraPopUpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _cameraPopUpTimer.Tick += OnCameraPopUpTimerTick;
        _cameraPopUpTimer.Start();
    }

    private void OnCameraPopUpTimerTick(object? sender, EventArgs e)
    {
        _cameraPopUpTimer?.Stop();

        // If the user moved their cursor directly into the notch to interact with controls, don't dismiss!
        if (_isMouseOverNotch || _vm.NotchState == NotchState.Open || _isContextMenuOpen)
            return;

        _isCameraPopUpActive = false;
        LiveActivity.IsPeeking = false;

        // Smoothly settle back to resting closed dimensions
        _widthSpring.Response = 0.32;
        _widthSpring.DampingFraction = 0.86;
        _heightSpring.Response = 0.30;
        _heightSpring.DampingFraction = 0.85;

        _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
        _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
        _topRadiusSpring.AnimateTo(NotchConstants.ClosedTopRadius, _currentTopRadius);
        _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        _shadowOpacitySpring.AnimateTo(ClosedShadowOpacity, _currentShadowOpacity);
    }

    #endregion

    #region Hardware Privacy Indicators

    private void UpdateCameraPrivacy(bool active, string? app)
    {
        CameraPrivacyDot.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CameraPrivacyDot.ToolTip = active ? $"Camera in use by {app ?? "an application"}" : null;
    }

    private void UpdateMicPrivacy(bool active, string? app)
    {
        MicrophonePrivacyDot.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        MicrophonePrivacyDot.ToolTip = active ? $"Microphone in use by {app ?? "an application"}" : null;
    }

    #endregion

    #region Countdown Focus Timer

    private void UpdateTimerDisplay(TimeSpan remaining, double progress)
    {
        TimerCompactText.Text = _services.Timer.FormattedTime;
        TimerCompactBadge.Visibility = Visibility.Visible;
    }

    private void UpdateTimerState(CountdownTimerState state)
    {
        TimerCompactBadge.Visibility = (state == CountdownTimerState.Running || state == CountdownTimerState.Paused)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnTimerCompleted()
    {
        TimerCompactBadge.Visibility = Visibility.Collapsed;
        ShowPowerSurge(100, false);
        PowerSurgeTitleText.Text = "Focus Session Completed!";
        PowerSurgeSubText.Text = "Great work! Time for a break.";
        PowerSurgeIconText.Text = "🎉";
        PowerSurgeIconBorder.Background = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
    }

    private void OnTimerBadgeClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_services.Timer.State == CountdownTimerState.Idle || _services.Timer.State == CountdownTimerState.Completed)
        {
            _services.Timer.Start(TimeSpan.FromMinutes(25), "Focus");
        }
        else if (_services.Timer.State == CountdownTimerState.Running)
        {
            _services.Timer.Pause();
        }
        else if (_services.Timer.State == CountdownTimerState.Paused)
        {
            _services.Timer.Resume();
        }
        e.Handled = true;
    }

    private void OnTimerBadgeRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _services.Timer.Reset();
        TimerCompactBadge.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }



    #endregion

    #region Power Surge & Battery Alerts

    private void OnChargingStateChanged(bool isCharging)
    {
        int percent = _services.Battery.ChargePercent;
        ShowPowerSurge(percent, isCharging);
    }

    public void ShowPowerSurge(int percent, bool isCharging)
    {
        _powerSurgeActive = true;
        if (isCharging)
        {
            PowerSurgeTitleText.Text = $"{percent}% Charging";
            PowerSurgeSubText.Text = "Power Connected";
            PowerSurgeIconText.Text = "⚡";
            PowerSurgeIconBorder.Background = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            PowerSurgeShadow.Color = Color.FromRgb(0x30, 0xD1, 0x58);
        }
        else if (percent <= 20)
        {
            PowerSurgeTitleText.Text = $"Battery Low ({percent}%)";
            PowerSurgeSubText.Text = "Connect to Power";
            PowerSurgeIconText.Text = "🪫";
            PowerSurgeIconBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
            PowerSurgeShadow.Color = Color.FromRgb(0xFF, 0x45, 0x3A);
        }
        else
        {
            PowerSurgeTitleText.Text = $"{percent}% Battery";
            PowerSurgeSubText.Text = "On Battery Power";
            PowerSurgeIconText.Text = "🔋";
            PowerSurgeIconBorder.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
            PowerSurgeShadow.Color = Color.FromRgb(0x0A, 0x84, 0xFF);
        }

        PowerSurgeCapsule.Visibility = Visibility.Visible;
        PowerSurgeCapsule.IsHitTestVisible = true;

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.36, 0.72);
            _heightSpring.SetPhysics(0.34, 0.76);
            _widthSpring.AnimateTo(280, _currentWidth);
            _heightSpring.AnimateTo(44, _currentHeight);
            _bottomRadiusSpring.AnimateTo(22, _currentBottomRadius);
        }

        _powerSurgeScaleSpring.AnimateTo(1.0, 0.86);
        _powerSurgeTranslateSpring.AnimateTo(0.0, -4.0);
        _powerSurgeOpacitySpring.AnimateTo(1.0, 0.0);

        UpdateNotchVisuals();

        _powerSurgeDismissTimer?.Stop();
        _powerSurgeDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _powerSurgeDismissTimer.Tick += (_, _) =>
        {
            _powerSurgeDismissTimer.Stop();
            DismissPowerSurge();
        };
        _powerSurgeDismissTimer.Start();
    }

    private void DismissPowerSurge()
    {
        _powerSurgeScaleSpring.AnimateTo(0.88, _currentPowerSurgeScale);
        _powerSurgeTranslateSpring.AnimateTo(-3.0, _currentPowerSurgeTranslate);
        _powerSurgeOpacitySpring.AnimateTo(0.0, _currentPowerSurgeOpacity);

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.32, 0.85);
            _heightSpring.SetPhysics(0.30, 0.85);
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
            _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        }

        var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        exitTimer.Tick += (_, _) =>
        {
            exitTimer.Stop();
            if (_powerSurgeActive && _currentPowerSurgeOpacity < 0.1)
            {
                PowerSurgeCapsule.Visibility = Visibility.Collapsed;
                PowerSurgeCapsule.IsHitTestVisible = false;
                _powerSurgeActive = false;
                UpdateClosedContentVisibility();
                UpdateNotchVisuals();
            }
        };
        exitTimer.Start();
        UpdateNotchVisuals();
    }

    #endregion

    #region Caps Lock & Num Lock HUD Morph

    public void ShowLockState(string lockName, bool isLocked)
    {
        _lockStateActive = true;
        LockStateTitleText.Text = $"{lockName} {(isLocked ? "ON" : "OFF")}";
        LockStateIconText.Text = isLocked ? "⇪" : "⇩";
        LockStateIconBorder.Background = isLocked 
            ? new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) 
            : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));

        LockStateCapsule.Visibility = Visibility.Visible;
        LockStateCapsule.IsHitTestVisible = true;

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.36, 0.72);
            _heightSpring.SetPhysics(0.34, 0.76);
            _widthSpring.AnimateTo(230, _currentWidth);
            _heightSpring.AnimateTo(44, _currentHeight);
            _bottomRadiusSpring.AnimateTo(22, _currentBottomRadius);
        }

        _lockStateScaleSpring.AnimateTo(1.0, 0.86);
        _lockStateTranslateSpring.AnimateTo(0.0, -4.0);
        _lockStateOpacitySpring.AnimateTo(1.0, 0.0);

        UpdateNotchVisuals();

        _lockStateDismissTimer?.Stop();
        _lockStateDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _lockStateDismissTimer.Tick += (_, _) =>
        {
            _lockStateDismissTimer.Stop();
            DismissLockState();
        };
        _lockStateDismissTimer.Start();
    }

    private void DismissLockState()
    {
        _lockStateScaleSpring.AnimateTo(0.88, _currentLockStateScale);
        _lockStateTranslateSpring.AnimateTo(-3.0, _currentLockStateTranslate);
        _lockStateOpacitySpring.AnimateTo(0.0, _currentLockStateOpacity);

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.32, 0.85);
            _heightSpring.SetPhysics(0.30, 0.85);
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
            _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        }

        var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        exitTimer.Tick += (_, _) =>
        {
            exitTimer.Stop();
            if (_lockStateActive && _currentLockStateOpacity < 0.1)
            {
                LockStateCapsule.Visibility = Visibility.Collapsed;
                LockStateCapsule.IsHitTestVisible = false;
                _lockStateActive = false;
                UpdateClosedContentVisibility();
                UpdateNotchVisuals();
            }
        };
        exitTimer.Start();
        UpdateNotchVisuals();
    }

    #endregion

    #region USB Drive Alerts

    public void ShowUsbDriveAlert(string driveLetter, string? label, long totalBytes, bool isConnected)
    {
        _activeUsbDriveLetter = driveLetter;
        _usbDriveActive = true;

        if (isConnected)
        {
            UsbDriveTitleText.Text = $"{label ?? "USB Drive"} ({driveLetter}:) Connected";
            UsbDriveSubText.Text = totalBytes > 0 ? $"{totalBytes / (1024L * 1024L * 1024L)} GB Total" : "Removable Storage";
            OpenUsbDriveButton.Visibility = Visibility.Visible;
        }
        else
        {
            UsbDriveTitleText.Text = $"Drive ({driveLetter}:) Disconnected";
            UsbDriveSubText.Text = "Safely Removed";
            OpenUsbDriveButton.Visibility = Visibility.Collapsed;
        }

        UsbDriveCapsule.Visibility = Visibility.Visible;
        UsbDriveCapsule.IsHitTestVisible = true;

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.36, 0.72);
            _heightSpring.SetPhysics(0.34, 0.76);
            _widthSpring.AnimateTo(320, _currentWidth);
            _heightSpring.AnimateTo(44, _currentHeight);
            _bottomRadiusSpring.AnimateTo(22, _currentBottomRadius);
        }

        _usbDriveScaleSpring.AnimateTo(1.0, 0.86);
        _usbDriveTranslateSpring.AnimateTo(0.0, -4.0);
        _usbDriveOpacitySpring.AnimateTo(1.0, 0.0);

        UpdateNotchVisuals();

        _usbDriveDismissTimer?.Stop();
        _usbDriveDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _usbDriveDismissTimer.Tick += (_, _) =>
        {
            _usbDriveDismissTimer.Stop();
            DismissUsbDriveAlert();
        };
        _usbDriveDismissTimer.Start();
    }

    private void DismissUsbDriveAlert()
    {
        _usbDriveScaleSpring.AnimateTo(0.88, _currentUsbDriveScale);
        _usbDriveTranslateSpring.AnimateTo(-3.0, _currentUsbDriveTranslate);
        _usbDriveOpacitySpring.AnimateTo(0.0, _currentUsbDriveOpacity);

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.32, 0.85);
            _heightSpring.SetPhysics(0.30, 0.85);
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
            _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        }

        var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        exitTimer.Tick += (_, _) =>
        {
            exitTimer.Stop();
            if (_usbDriveActive && _currentUsbDriveOpacity < 0.1)
            {
                UsbDriveCapsule.Visibility = Visibility.Collapsed;
                UsbDriveCapsule.IsHitTestVisible = false;
                _usbDriveActive = false;
                UpdateClosedContentVisibility();
                UpdateNotchVisuals();
            }
        };
        exitTimer.Start();
        UpdateNotchVisuals();
    }

    private void OnOpenUsbDriveClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_activeUsbDriveLetter))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"{_activeUsbDriveLetter}:\\",
                    UseShellExecute = true
                });
            }
            catch { }
            DismissUsbDriveAlert();
        }
    }

    #endregion

    #region Armoury Crate Operating Mode Alert Pop-up

    public void ShowOperatingModeAlert(ArmouryOperatingMode mode)
    {
        var details = ArmouryCrateService.GetModeDetails(mode);
        _operatingModeActive = true;

        OperatingModeTitleText.Text = $"{details.Title.ToUpperInvariant()} MODE";
        OperatingModeSubText.Text = details.Description;
        OperatingModeIconText.Text = details.Icon;

        // Push transient pop-up notification into system notification service & history
        _services.Notifications.AddNotification("Operating Mode", $"{details.Title} Mode Activated", details.Description);

        Color accentColor;
        try
        {
            accentColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(details.AccentHex);
        }
        catch
        {
            accentColor = Color.FromRgb(0x0A, 0x84, 0xFF);
        }

        OperatingModeIconBorder.Background = new SolidColorBrush(accentColor);
        OperatingModeShadow.Color = accentColor;

        if (_vm.NotchState == NotchState.Open)
        {
            // When notch is open, show smooth floating alert banner at top
            OpenOperatingModeIcon.Text = details.Icon;
            OpenOperatingModeTitle.Text = $"{details.Title.ToUpperInvariant()} MODE";
            OpenOperatingModeSub.Text = details.Description;
            OpenOperatingModeToast.Visibility = Visibility.Visible;

            var daOpacity = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220));
            var daTranslate = new DoubleAnimation(-6.0, 0.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var daScale = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(260));

            OpenOperatingModeToast.BeginAnimation(UIElement.OpacityProperty, daOpacity);
            OpenOperatingModeTranslate.BeginAnimation(TranslateTransform.YProperty, daTranslate);
            OpenOperatingModeScale.BeginAnimation(ScaleTransform.ScaleXProperty, daScale);
            OpenOperatingModeScale.BeginAnimation(ScaleTransform.ScaleYProperty, daScale);
        }
        else
        {
            OperatingModeCapsule.Visibility = Visibility.Visible;
            OperatingModeCapsule.IsHitTestVisible = true;

            _widthSpring.SetPhysics(0.36, 0.72);
            _heightSpring.SetPhysics(0.34, 0.76);
            _widthSpring.AnimateTo(285, _currentWidth);
            _heightSpring.AnimateTo(44, _currentHeight);
            _bottomRadiusSpring.AnimateTo(22, _currentBottomRadius);

            _operatingModeScaleSpring.AnimateTo(1.0, 0.86);
            _operatingModeTranslateSpring.AnimateTo(0.0, -4.0);
            _operatingModeOpacitySpring.AnimateTo(1.0, 0.0);

            UpdateNotchVisuals();
        }

        _operatingModeDismissTimer?.Stop();
        _operatingModeDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _operatingModeDismissTimer.Tick += (_, _) =>
        {
            _operatingModeDismissTimer.Stop();
            DismissOperatingModeAlert();
        };
        _operatingModeDismissTimer.Start();
    }

    private void DismissOperatingModeAlert()
    {
        if (OpenOperatingModeToast.Visibility == Visibility.Visible)
        {
            var daOpacity = new DoubleAnimation(OpenOperatingModeToast.Opacity, 0.0, TimeSpan.FromMilliseconds(220));
            var daTranslate = new DoubleAnimation(OpenOperatingModeTranslate.Y, -6.0, TimeSpan.FromMilliseconds(220));
            daOpacity.Completed += (_, _) =>
            {
                OpenOperatingModeToast.Visibility = Visibility.Collapsed;
            };
            OpenOperatingModeToast.BeginAnimation(UIElement.OpacityProperty, daOpacity);
            OpenOperatingModeTranslate.BeginAnimation(TranslateTransform.YProperty, daTranslate);
        }

        _operatingModeScaleSpring.AnimateTo(0.88, _currentOperatingModeScale);
        _operatingModeTranslateSpring.AnimateTo(-3.0, _currentOperatingModeTranslate);
        _operatingModeOpacitySpring.AnimateTo(0.0, _currentOperatingModeOpacity);

        if (_vm.NotchState != NotchState.Open)
        {
            _widthSpring.SetPhysics(0.32, 0.85);
            _heightSpring.SetPhysics(0.30, 0.85);
            _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            _heightSpring.AnimateTo(NotchConstants.ClosedHeight, _currentHeight);
            _bottomRadiusSpring.AnimateTo(NotchConstants.ClosedBottomRadius, _currentBottomRadius);
        }

        var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        exitTimer.Tick += (_, _) =>
        {
            exitTimer.Stop();
            if (_operatingModeActive && _currentOperatingModeOpacity < 0.1)
            {
                OperatingModeCapsule.Visibility = Visibility.Collapsed;
                OperatingModeCapsule.IsHitTestVisible = false;
                _operatingModeActive = false;
                UpdateClosedContentVisibility();
                UpdateNotchVisuals();
            }
        };
        exitTimer.Start();
        UpdateNotchVisuals();
    }

    #endregion

    #region Middle-Click Play/Pause Gesture

    private void OnRootGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _ = _mediaService.PlayPauseAsync();
            e.Handled = true;
        }
    }

    #endregion

}
