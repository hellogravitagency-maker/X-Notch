using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using XNotch.Services;

namespace XNotch.Models;

/// <summary>
/// Hover interaction mode for the notch.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HoverMode
{
    /// <summary>Hover slightly expands, click fully opens.</summary>
    HoverPeekClickOpen,
    /// <summary>Long hover fully opens, click also opens.</summary>
    LongHoverOpen
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    Dark,
    Light,
    LiquidGlass,
    Auto,
    /// <summary>Frosted glassmorphism: blurred desktop behind a dark translucent tint.</summary>
    Glass,
    /// <summary>Monochrome graphite greys with a graphite accent.</summary>
    Graphite,
    /// <summary>Pure black, blends into a real camera notch on OLED panels.</summary>
    Oled
}

/// <summary>
/// User-configurable settings, persisted to %AppData%/X-Notch/settings.json.
/// </summary>
public class AppSettings
{
    // General
    public bool StartOnBoot { get; set; }
    public HoverMode HoverMode { get; set; } = HoverMode.HoverPeekClickOpen;
    public int HoverOpenDelayMs { get; set; } = 100;
    public int HoverCloseDelayMs { get; set; } = 150;
    public int LongHoverDelayMs { get; set; } = 600;

    /// <summary>Tucks the notch into a subtle edge sliver when idle.</summary>
    public bool AutoHide { get; set; } = false;

    /// <summary>Summons the island popup centered around the current mouse cursor location.</summary>
    public bool CursorCenterPlacement { get; set; } = false;

    /// <summary>When cursor is placed directly under the top camera, briefly pops up the notch for 1-2s.</summary>
    public bool CameraHoverPopUp { get; set; } = true;
    public int CameraPopUpDurationMs { get; set; } = 1800;

    // Updates
    public bool AutoCheckForUpdates { get; set; } = true;

    // Screens & position
    /// <summary>Where the main island was last docked (persisted across restarts).</summary>
    public NotchDock NotchDockPosition { get; set; } = NotchDock.Top;

    /// <summary>Device name of the screen hosting the main island (null = primary).</summary>
    public string? NotchScreenDevice { get; set; }

    /// <summary>Show an island on every connected screen.</summary>
    public bool ShowOnAllScreens { get; set; }

    /// <summary>When multiple islands are shown, open/close them together.</summary>
    public bool SyncIslandsOpenState { get; set; } = true;

    /// <summary>
    /// Reserve a dedicated strip at the top of the screen (shell AppBar, like the
    /// taskbar) so maximized windows start below the notch. Opt-in.
    /// </summary>
    public bool ReserveSpaceAtTop { get; set; }

    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.Auto;
    public bool AdaptToWindowsTheme { get; set; } = true;
    public double CornerRadiusScale { get; set; } = 1.0;

    // Motion (applied to every SpringAnimator)
    public double AnimationSpeed { get; set; } = 1.0;
    public double SpringIntensity { get; set; } = 1.0;
    public bool ReduceMotion { get; set; }
    public bool ShowAlbumGlow { get; set; } = true;
    public bool ShowShadow { get; set; } = false;

    /// <summary>Accent colour as #RRGGBB, or null to follow the Windows accent.</summary>
    public string? AccentColor { get; set; }

    // Dock (macOS style)
    public bool DockAutoHide { get; set; } = true;
    public bool DockMagnification { get; set; } = true;
    public double DockIconSize { get; set; } = 48;
    public double DockMagnifiedScale { get; set; } = 1.6;

    /// <summary>Genie animation when X-Notch's own windows minimize into the dock.</summary>
    public bool GenieEffect { get; set; } = true;

    /// <summary>Also play the genie when any app window minimizes, into its dock icon.</summary>
    public bool GenieAllWindows { get; set; } = true;

    // Music
    public bool ShowMusicControls { get; set; } = true;
    public bool ShowVisualizer { get; set; } = true;

    // HUD
    public bool ShowVolumeHud { get; set; } = true;
    public bool ShowBrightnessHud { get; set; } = true;

    // Calendar
    public bool ShowCalendar { get; set; } = true;

    // Battery
    public bool ShowBattery { get; set; } = true;

    // Webcam
    public bool ShowWebcam { get; set; } = false;
    public int WebcamFps { get; set; } = 15;

    // Open Notch Widget Carousel Toggles
    public bool ShowMusicCard { get; set; } = true;
    public bool ShowStopwatchCard { get; set; } = true;
    public bool ShowWaterCard { get; set; } = true;
    public bool ShowSleepCard { get; set; } = true;
    public bool ShowTasksCard { get; set; } = true;
    public bool ShowVitalsCard { get; set; } = true;
    public bool ShowNotificationsCard { get; set; } = true;
    public bool ShowAgentsCard { get; set; } = true;
    public bool ShowClipboardCard { get; set; } = true;
    public bool ShowControlsCard { get; set; } = true;
    public bool ShowWidgetsCard { get; set; } = true;
    public bool ShowCaptureCard { get; set; } = true;
    public bool ShowDeckCard { get; set; } = true;
    public bool ShowStreamCard { get; set; } = true;

    /// <summary>Looping muted video behind the desktop icons (primary screen); pauses when covered.</summary>
    public bool LiveWallpaper { get; set; }
    public string? LiveWallpaperFile { get; set; }
    public double LiveWallpaperBrightness { get; set; } = 1.0;

    /// <summary>Replace Windows' Alt-Tab with X-Notch's thumbnail switcher.</summary>
    public bool AltTabEnabled { get; set; }

    // Streaming. Secrets are DPAPI-encrypted (only this Windows user can read them).
    public string? ChatTwitch { get; set; }
    public string? ChatYouTubeKeyProtected { get; set; }
    public string? ChatYouTubeVideo { get; set; }
    public bool ChatOverlay { get; set; }
    public bool ObsEnabled { get; set; }
    public int ObsPort { get; set; } = 4455;
    public string? ObsPasswordProtected { get; set; }

    public static string? Protect(string? secret) => string.IsNullOrEmpty(secret) ? null :
        Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(secret), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(stored), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    /// <summary>Phone Deck: LAN web remote. Off by default; never runs on Public networks.</summary>
    public bool DeckEnabled { get; set; }
    public List<DeckDevice> DeckDevices { get; set; } = [];

    /// <summary>New screenshots and screen recordings saved by Snipping Tool go onto the shelf.</summary>
    public bool CapturesToShelf { get; set; } = true;

    // Widgets: weather/air via Open-Meteo, currency via Frankfurter (ECB). Only the typed city is sent.
    public string? WeatherCity { get; set; }
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    public bool WeatherFahrenheit { get; set; }
    public string CurrencyPair { get; set; } = "USD/EUR";
    public string PingHost { get; set; } = "1.1.1.1";
    public string? CountdownTitle { get; set; }
    public string? CountdownDate { get; set; }
    public string WorldClockZone { get; set; } = "UTC";
    public PetKind PetKind { get; set; } = PetKind.Cat;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public string? ScreenTimeDate { get; set; }
    public long ScreenTimeSeconds { get; set; }

    /// <summary>Clipboard history (DPAPI-encrypted on disk; password-manager copies are never kept).</summary>
    public bool ClipboardEnabled { get; set; } = true;
    public int ClipboardKeepHours { get; set; } = 24;
    public int ClipboardHistorySize { get; set; } = 50;

    /// <summary>Live Claude Code / Codex status. Turning it on installs reporting hooks (backed up first).</summary>
    public bool AgentsEnabled { get; set; }

    /// <summary>macOS-style dock at the bottom. While on, the Windows taskbar is hidden (restored when off/exit).</summary>
    public bool DockEnabled { get; set; }

    /// <summary>UI language code, or "auto" to follow Windows. Applies on the next start.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>A dock on every screen (otherwise only the main screen).</summary>
    public bool DockEveryScreen { get; set; }

    /// <summary>Each dock shows only the apps whose windows are on its screen.</summary>
    public bool DockAppsOnOwnScreen { get; set; } = true;

    /// <summary>Per-app island behaviour, keyed by lower-case exe name (e.g. "valorant.exe").</summary>
    public Dictionary<string, AppRule> AppRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the island does when any other app is fullscreen on its screen.</summary>
    public AppRule FullscreenRule { get; set; } = AppRule.Hide;

    /// <summary>Executable paths kept in the dock, in order.</summary>
    public List<string> DockPins { get; set; } =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
    ];

    // Workspace Roots for Project and Task Discovery
    public List<string> WorkspaceRoots { get; set; } = [];

    // Water Tracker State
    public int WaterCurrentMl { get; set; } = 750;
    public int WaterGoalMl { get; set; } = 2000;
    public string WaterLastLogDate { get; set; } = "";

    // Sleep Tracker State
    public double SleepTargetHours { get; set; } = 8.0;
    public string SleepBedtime { get; set; } = "23:00";
    public string SleepWakeTime { get; set; } = "07:00";
    public double SleepLastNightHours { get; set; } = 7.5;
    public bool SleepWindDownEnabled { get; set; } = true;

    // Beta features
    public bool UseLiquidGlassTheme { get; set; } = false;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X-Notch");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            string pathToRead = SettingsPath;
            if (File.Exists(pathToRead))
            {
                var json = File.ReadAllText(pathToRead);
                if (json.Contains("\"White\"") || json.Contains("\"Custom\""))
                {
                    json = json.Replace("\"White\"", "\"Light\"").Replace("\"Custom\"", "\"Dark\"");
                }
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

                using var document = JsonDocument.Parse(json);
                bool hasAdaptSetting = document.RootElement.TryGetProperty(nameof(AdaptToWindowsTheme), out _);

                if (!hasAdaptSetting)
                    settings.AdaptToWindowsTheme = settings.Theme == AppTheme.Auto;

                if (settings.Theme == AppTheme.Auto)
                    settings.Theme = AppTheme.Dark;

                return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    private static readonly object SaveLock = new();
    private static System.Threading.Timer? _saveDebounceTimer;

    public void Save(bool immediate = false)
    {
        if (immediate)
        {
            ExecuteSave();
            return;
        }

        lock (SaveLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = new System.Threading.Timer(_ => ExecuteSave(), null, 250, System.Threading.Timeout.Infinite);
        }
    }

    private void ExecuteSave()
    {
        lock (SaveLock)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(this, JsonOptions);

                var tempPath = SettingsPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, SettingsPath, overwrite: true);
            }
            catch { }
        }
    }

    public AppTheme GetManualTheme()
    {
        return Theme == AppTheme.Auto ? AppTheme.Dark : Theme;
    }

    public AppTheme GetRequestedTheme()
    {
        return AdaptToWindowsTheme ? AppTheme.Auto : GetManualTheme();
    }

    /// <summary>Register or unregister the app from Windows startup.</summary>
    public void ApplyStartOnBoot()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "X-Notch";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true);
            if (key == null) return;

            if (StartOnBoot)
            {
                var exePath = Environment.ProcessPath;
                if (exePath != null)
                    key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch { }
    }
}
