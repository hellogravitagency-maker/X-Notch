using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using XNotch.Models;
using XNotch.Services;
using XNotch.Views;
using Forms = System.Windows.Forms;
using DrawColor = System.Drawing.Color;

namespace XNotch;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private NotchWindow? _notchWindow;
    private readonly Dictionary<string, NotchWindow> _secondaryWindows = new();
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private AppSettings _settings = null!;
    private ThemeService _themeService = null!;
    private NotchServices _services = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handler for diagnostics
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogCrash(ex);
            try { TaskbarHelper.Restore(); } catch { } // never leave the user without a taskbar
        };
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            System.Windows.MessageBox.Show(args.Exception.ToString(), "X-Notch Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Single instance enforcement
        const string mutexName = "XNotch_SingleInstance_Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        _ownsMutex = createdNew;
        LogTrace($"OnStartup: createdNew={createdNew}, _ownsMutex={_ownsMutex}");
        if (!createdNew)
        {
            // The named mutex already exists. Normally that means another instance is
            // running — but it can also be a stale mutex left behind by a previous
            // instance that was force-killed during a self-update. Briefly try to
            // acquire it: if we succeed (including via an abandoned mutex, which
            // happens when the old owner died without releasing), we are the rightful
            // owner and continue. Otherwise a live instance holds it, so we exit.
            bool acquired;
            try
            {
                acquired = _mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                // Previous owner terminated without releasing — ownership is now ours.
                acquired = true;
            }

            if (acquired)
            {
                _ownsMutex = true;
                LogTrace("OnStartup: Existing mutex acquired.");
            }
            else
            {
                LogTrace("OnStartup: Mutex not acquired -> Shutdown()");
                _mutex.Dispose();
                _mutex = null;
                Shutdown();
                return;
            }
        }

        try
        {
            // Load settings
            _settings = AppSettings.Load();
            Helpers.Loc.Init(_settings.Language); // before any window is built
            LogTrace($"Settings loaded: Theme={_settings.Theme}, AutoHide={_settings.AutoHide}");

            // Initialize theme
            _themeService = new ThemeService();
            _themeService.Apply(_settings);
            _themeService.StartWatchingSystemTheme();
            _themeService.ThemeChanged += ApplyTrayTheme;

            // System tray icon
            SetupTrayIcon();

            // Shared system services — one set for every island window
            _services = new NotchServices();

            // Launch the primary island (on its remembered screen)
            _notchWindow = new NotchWindow(_settings, _themeService, _services,
                isPrimary: true, screenDevice: _settings.NotchScreenDevice);
            WireIslandEvents(_notchWindow);
            _notchWindow.Show();
            LogTrace("Primary island shown.");

            // Optional islands on every other screen
            RebuildSecondaryWindows();

            // Dock (replaces the taskbar while on); also undoes a hidden taskbar left by a crash
            UpdateDock();

            // Genie: minimizing pours windows into their dock icon
            _genie = new GenieService(_settings, DockPointFor, () => _docks.FirstOrDefault()?.IconPixels ?? 48);

            // React to monitors being plugged/unplugged or rearranged
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // Silent update check shortly after startup (network failures are ignored)
            if (_settings.AutoCheckForUpdates)
                _ = CheckForUpdatesSilentlyAsync();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _notchWindow?.HandleDisplayChange();
            RebuildSecondaryWindows();
            UpdateDock();
        });
    }

    private void WireIslandEvents(NotchWindow window)
    {
        // Settings window requested from island gear or shortcut
        window.SettingsRequested += OpenSettings;

        // Settings changed on one island → apply everywhere + re-evaluate islands
        window.SettingsUpdatedByUser += settings => ApplyAll(settings, except: window);

        // Open/close sync across islands (when enabled)
        window.UserStateChanged += state =>
        {
            if (!_settings.SyncIslandsOpenState) return;
            if (state != NotchState.Open && state != NotchState.Closed) return;

            foreach (var other in AllIslands())
            {
                if (!ReferenceEquals(other, window))
                    other.ApplySyncedState(state);
            }
        };
    }

    private IEnumerable<NotchWindow> AllIslands()
    {
        if (_notchWindow != null)
            yield return _notchWindow;
        foreach (var window in _secondaryWindows.Values)
            yield return window;
    }

    /// <summary>
    /// Creates/removes secondary islands so that — when ShowOnAllScreens is on —
    /// every screen except the primary island's has exactly one island.
    /// </summary>
    private void RebuildSecondaryWindows()
    {
        var wanted = new HashSet<string>();
        if (_settings.ShowOnAllScreens && _notchWindow != null)
        {
            foreach (var screen in Forms.Screen.AllScreens)
            {
                if (screen.DeviceName != _notchWindow.ScreenDevice)
                    wanted.Add(screen.DeviceName);
            }
        }

        // Close islands whose screen disappeared, moved under the primary,
        // or that the setting no longer wants
        foreach (var device in _secondaryWindows.Keys.ToList())
        {
            if (!wanted.Contains(device))
            {
                _secondaryWindows[device].Close();
                _secondaryWindows.Remove(device);
            }
        }

        // Spawn islands for newly wanted screens
        foreach (var device in wanted)
        {
            if (_secondaryWindows.ContainsKey(device))
                continue;

            var island = new NotchWindow(_settings, _themeService, _services,
                isPrimary: false, screenDevice: device);
            WireIslandEvents(island);
            _secondaryWindows[device] = island;
            island.Show();
        }
    }

    private async System.Threading.Tasks.Task CheckForUpdatesSilentlyAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(15));

            var updateService = new UpdateService();
            var result = await updateService.CheckForUpdateAsync();

            if (result == UpdateCheckResult.UpdateAvailable)
            {
                if (_trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(
                        5000,
                        "X-Notch update available",
                        $"Version {updateService.LatestVersion} is ready. Open Settings → Check for Updates to install.",
                        Forms.ToolTipIcon.Info);
                }

                _notchWindow?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _notchWindow.ShowNotification(new Models.NotificationItem(
                        999999,
                        "X-Notch",
                        "Update Available",
                        $"Version {updateService.LatestVersion} is ready to install.",
                        DateTime.Now));
                }));
            }
        }
        catch
        {
            // Never let a background check surface an error.
        }
    }

    public static void LogTrace(string message)
    {
        try
        {
            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "X-Notch", "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(logDir, "app.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [PID {Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void LogCrash(Exception exception)
    {
        try
        {
            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "X-Notch", "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(logDir, "crash.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never crash the handler.
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = GetAppIcon(),
            Text = "X-Notch",
            Visible = true
        };

        _trayMenu = new Forms.ContextMenuStrip
        {
            Renderer = new ThemedMenuRenderer(() => _themeService.CurrentPalette),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Forms.Padding(4, 6, 4, 6),
            Font = new Font("Segoe UI", 9.5f)
        };

        var popupCursorItem = new Forms.ToolStripMenuItem("Pop-up at Cursor")
        {
            Padding = new Forms.Padding(12, 6, 20, 6)
        };
        popupCursorItem.Click += (_, _) => _notchWindow?.PopUpAtCursor();

        var settingsItem = new Forms.ToolStripMenuItem("Settings")
        {
            Padding = new Forms.Padding(12, 6, 20, 6)
        };
        settingsItem.Click += (_, _) => OpenSettings();

        var separator = new Forms.ToolStripSeparator();

        var quitItem = new Forms.ToolStripMenuItem("Quit")
        {
            Padding = new Forms.Padding(12, 6, 20, 6)
        };
        quitItem.Click += (_, _) => QuitApp();

        _trayMenu.Items.Add(popupCursorItem);
        _trayMenu.Items.Add(settingsItem);
        _trayMenu.Items.Add(separator);
        _trayMenu.Items.Add(quitItem);

        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
        ApplyTrayTheme();
    }

    private static Icon GetAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }

            string baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory ?? "";
            if (!string.IsNullOrEmpty(baseDir))
            {
                string iconPath = System.IO.Path.Combine(baseDir, "Assets", "Icons", "XNotch.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }
            }
        }
        catch { }

        return CreateDefaultIcon();
    }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon? _cachedDefaultIcon;

    private static Icon CreateDefaultIcon()
    {
        if (_cachedDefaultIcon != null) return _cachedDefaultIcon;
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillEllipse(System.Drawing.Brushes.White, 1, 1, 14, 14);
        IntPtr hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        _cachedDefaultIcon = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        return _cachedDefaultIcon;
    }

    private SettingsWindow? _settingsWindow;
    private GenieService? _genie;

    /// <summary>Opens (or brings forward) the macOS-style Settings window.</summary>
    internal void OpenSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_settings, _themeService, s => ApplyAll(s), () => _notchWindow?.OpenPluginManager(),
                () => _services.Foreground.Recent);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>One place that pushes changed settings to every surface.</summary>
    private void ApplyAll(AppSettings settings, NotchWindow? except = null)
    {
        _settings = settings;
        foreach (var island in AllIslands().ToList())
            if (!ReferenceEquals(island, except)) island.ApplySettings(settings);
        RebuildSecondaryWindows();
        UpdateDock();
        if (_genie != null) _genie.Settings = settings;
    }

    /// <summary>Genie target: the window's app icon in the dock on its screen (physical px).</summary>
    private System.Drawing.Point? DockPointFor(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out int pid);
        var path = Native.ProcessPath(pid);
        if (path == null) return null;
        string key = AppIdentity.Key(hwnd, path);
        var screen = Forms.Screen.FromHandle(hwnd).DeviceName;
        return _docks.FirstOrDefault(d => d.Screen.DeviceName == screen)?.IconPoint(key)
            ?? _docks.Select(d => d.IconPoint(key)).FirstOrDefault(p => p != null);
    }

    private void QuitApp()
    {
        LogTrace("QuitApp called.");
        _themeService.ThemeChanged -= ApplyTrayTheme;
        _themeService?.StopWatchingSystemTheme();
        _trayIcon?.Dispose();
        foreach (var window in _secondaryWindows.Values.ToList())
            window.Close();
        _secondaryWindows.Clear();
        _notchWindow?.Close();
        Shutdown();
    }

    private readonly List<DockWindow> _docks = [];

    /// <summary>One dock per screen (or just the main one). Restores the taskbar when the last closes.</summary>
    private void UpdateDock()
    {
        var screens = !_settings.DockEnabled ? []
            : _settings.DockEveryScreen ? Forms.Screen.AllScreens.ToList()
            : [Forms.Screen.PrimaryScreen!];

        foreach (var d in _docks.Where(d => screens.All(s => s.DeviceName != d.Screen.DeviceName)).ToList())
        {
            d.Close();
            _docks.Remove(d);
        }
        foreach (var s in screens)
        {
            var dock = _docks.FirstOrDefault(d => d.Screen.DeviceName == s.DeviceName);
            if (dock == null)
            {
                dock = new DockWindow(_settings, s);
                dock.Show();
                _docks.Add(dock);
            }
            else { dock.MoveTo(s); dock.ApplySettings(_settings); } // resolution / arrangement / settings changed
        }

        if (_services != null)
        {
            _services.Fullscreen.FullscreenChanged -= OnFullscreenForDock;
            if (_docks.Count > 0) _services.Fullscreen.FullscreenChanged += OnFullscreenForDock;
        }
        if (_docks.Count == 0) TaskbarHelper.Restore();
    }

    private void OnFullscreenForDock(bool fullscreen) => Dispatcher.BeginInvoke(() =>
    {
        foreach (var d in _docks)
        {
            // Hide only the dock on the screen that has the fullscreen app.
            bool here = fullscreen && _services != null && _services.Fullscreen.FullscreenMonitor ==
                Helpers.ScreenHelper.GetMonitorHandle(d);
            d.Visibility = here ? Visibility.Hidden : Visibility.Visible;
        }
    });

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var d in _docks) d.Close();
        _docks.Clear();
        TaskbarHelper.Restore();
        _genie?.Dispose();
        LogTrace($"OnExit: ApplicationExitCode={e.ApplicationExitCode}");
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_themeService != null)
            _themeService.ThemeChanged -= ApplyTrayTheme;
        _trayIcon?.Dispose();
        _services?.Dispose();
        if (_ownsMutex && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Another shutdown path may already have released the mutex.
            }
        }

        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;
        base.OnExit(e);
    }

    private void ApplyTrayTheme()
    {
        if (_trayMenu == null) return;

        var palette = _themeService.CurrentPalette;
        var backColor = ToDrawingColor(palette.SurfaceRaised);
        var foreground = ToDrawingColor(palette.TextPrimary);

        _trayMenu.BackColor = backColor;
        _trayMenu.ForeColor = foreground;
        foreach (Forms.ToolStripItem item in _trayMenu.Items)
        {
            item.BackColor = backColor;
            item.ForeColor = foreground;
        }
    }

    private static DrawColor ToDrawingColor(System.Windows.Media.Color color)
    {
        return DrawColor.FromArgb(color.A, color.R, color.G, color.B);
    }
}

/// <summary>
/// Custom renderer for dark-themed ContextMenuStrip.
/// </summary>
internal class ThemedMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private readonly Func<ThemePalette> _getPalette;

    public ThemedMenuRenderer(Func<ThemePalette> getPalette) : base(new ThemedColorTable(getPalette))
    {
        _getPalette = getPalette;
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(ToDrawingColor(_getPalette().SurfaceRaised));
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(ToDrawingColor(_getPalette().Border));
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        var rc = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        if (e.Item.Selected)
        {
            using var brush = new SolidBrush(ToDrawingColor(_getPalette().HoverOverlay));
            using var gp = RoundedRect(rc, 4);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, gp);
        }
        else
        {
            using var brush = new SolidBrush(ToDrawingColor(_getPalette().SurfaceRaised));
            e.Graphics.FillRectangle(brush, rc);
        }
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(ToDrawingColor(_getPalette().Border));
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        var palette = _getPalette();
        e.TextColor = e.Item.Selected
            ? ToDrawingColor(palette.TextPrimary)
            : ToDrawingColor(palette.TextSecondary);
        base.OnRenderItemText(e);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static DrawColor ToDrawingColor(System.Windows.Media.Color color)
    {
        return DrawColor.FromArgb(color.A, color.R, color.G, color.B);
    }
}

internal class ThemedColorTable : Forms.ProfessionalColorTable
{
    private readonly Func<ThemePalette> _getPalette;

    public ThemedColorTable(Func<ThemePalette> getPalette)
    {
        _getPalette = getPalette;
    }

    public override DrawColor MenuBorder => ToDrawingColor(_getPalette().Border);
    public override DrawColor MenuItemBorder => DrawColor.Transparent;
    public override DrawColor MenuItemSelected => ToDrawingColor(_getPalette().HoverOverlay);
    public override DrawColor MenuStripGradientBegin => ToDrawingColor(_getPalette().SurfaceRaised);
    public override DrawColor MenuStripGradientEnd => ToDrawingColor(_getPalette().SurfaceRaised);
    public override DrawColor MenuItemSelectedGradientBegin => ToDrawingColor(_getPalette().HoverOverlay);
    public override DrawColor MenuItemSelectedGradientEnd => ToDrawingColor(_getPalette().HoverOverlay);
    public override DrawColor MenuItemPressedGradientBegin => ToDrawingColor(_getPalette().PressedOverlay);
    public override DrawColor MenuItemPressedGradientEnd => ToDrawingColor(_getPalette().PressedOverlay);
    public override DrawColor ImageMarginGradientBegin => ToDrawingColor(_getPalette().SurfaceRaised);
    public override DrawColor ImageMarginGradientMiddle => ToDrawingColor(_getPalette().SurfaceRaised);
    public override DrawColor ImageMarginGradientEnd => ToDrawingColor(_getPalette().SurfaceRaised);
    public override DrawColor ToolStripDropDownBackground => ToDrawingColor(_getPalette().SurfaceRaised);
    public override DrawColor SeparatorDark => ToDrawingColor(_getPalette().Border);
    public override DrawColor SeparatorLight => DrawColor.Transparent;

    private static DrawColor ToDrawingColor(System.Windows.Media.Color color)
    {
        return DrawColor.FromArgb(color.A, color.R, color.G, color.B);
    }
}

