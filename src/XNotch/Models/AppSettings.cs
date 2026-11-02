using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    Auto
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
    public bool ShowShadow { get; set; } = false;

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
        return Theme switch
        {
            AppTheme.Light => AppTheme.Light,
            AppTheme.LiquidGlass => AppTheme.LiquidGlass,
            _ => AppTheme.Dark
        };
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
