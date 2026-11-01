using System;
using System.IO;
using System.Windows;
using XNotch.Models;
using XNotch.Services;

namespace XNotch.Plugins;

/// <summary>
/// Implementation of IPluginContext that provides plugins access to X-Notch services.
/// </summary>
internal class PluginContext : IPluginContext
{
    private readonly Dictionary<Type, object> _services = new();

    public Window MainWindow { get; }
    public NotchViewModel NotchViewModel { get; }
    public MediaService MediaService { get; }
    public ThemeService ThemeService { get; }
    public VolumeService VolumeService { get; }
    public BrightnessService BrightnessService { get; }
    public BatteryService BatteryService { get; }
    public CalendarService CalendarService { get; }
    public AudioCaptureService AudioCaptureService { get; }
    public ShelfService ShelfService { get; }
    public WebcamService WebcamService { get; }
    public FullscreenService FullscreenService { get; }
    public AppSettings Settings { get; }

    public PluginContext(
        Window mainWindow,
        NotchViewModel notchViewModel,
        MediaService mediaService,
        ThemeService themeService,
        VolumeService volumeService,
        BrightnessService brightnessService,
        BatteryService batteryService,
        CalendarService calendarService,
        AudioCaptureService audioCaptureService,
        ShelfService shelfService,
        WebcamService webcamService,
        FullscreenService fullscreenService,
        AppSettings settings)
    {
        MainWindow = mainWindow;
        NotchViewModel = notchViewModel;
        MediaService = mediaService;
        ThemeService = themeService;
        VolumeService = volumeService;
        BrightnessService = brightnessService;
        BatteryService = batteryService;
        CalendarService = calendarService;
        AudioCaptureService = audioCaptureService;
        ShelfService = shelfService;
        WebcamService = webcamService;
        FullscreenService = fullscreenService;
        Settings = settings;
    }

    public string GetPluginDataPath(string pluginId)
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "X-Notch",
            "Plugins",
            pluginId);

        Directory.CreateDirectory(basePath);
        return basePath;
    }

    public void RegisterService<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }

    public T? GetService<T>() where T : class
    {
        return _services.TryGetValue(typeof(T), out var service) ? service as T : null;
    }

    public void Log(string message, PluginLogLevel level = PluginLogLevel.Info)
    {
        var prefix = level switch
        {
            PluginLogLevel.Debug => "[DEBUG]",
            PluginLogLevel.Info => "[INFO]",
            PluginLogLevel.Warning => "[WARN]",
            PluginLogLevel.Error => "[ERROR]",
            _ => "[INFO]"
        };

        System.Diagnostics.Debug.WriteLine($"{prefix} [Plugin] {message}");

        // Optionally write to a log file
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "X-Notch",
                "plugin.log");

            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {prefix} {message}\n");
        }
        catch
        {
            // Ignore logging errors
        }
    }
}
