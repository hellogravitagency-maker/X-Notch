using System;
using System.Windows;
using XNotch.Models;
using XNotch.Services;

namespace XNotch.Plugins;

/// <summary>
/// Context object provided to plugins, giving access to X-Notch services and APIs.
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Access to the main NotchWindow for UI manipulation.
    /// </summary>
    Window MainWindow { get; }

    /// <summary>
    /// Access to the NotchViewModel for state management.
    /// </summary>
    NotchViewModel NotchViewModel { get; }

    /// <summary>
    /// Access to media playback information and controls.
    /// </summary>
    MediaService MediaService { get; }

    /// <summary>
    /// Access to theme management.
    /// </summary>
    ThemeService ThemeService { get; }

    /// <summary>
    /// Access to volume control.
    /// </summary>
    VolumeService VolumeService { get; }

    /// <summary>
    /// Access to brightness control.
    /// </summary>
    BrightnessService BrightnessService { get; }

    /// <summary>
    /// Access to battery information.
    /// </summary>
    BatteryService BatteryService { get; }

    /// <summary>
    /// Access to calendar events.
    /// </summary>
    CalendarService CalendarService { get; }

    /// <summary>
    /// Access to audio visualization.
    /// </summary>
    AudioCaptureService AudioCaptureService { get; }

    /// <summary>
    /// Access to shelf file management.
    /// </summary>
    ShelfService ShelfService { get; }

    /// <summary>
    /// Access to webcam service.
    /// </summary>
    WebcamService WebcamService { get; }

    /// <summary>
    /// Access to fullscreen detection.
    /// </summary>
    FullscreenService FullscreenService { get; }

    /// <summary>
    /// Access to application settings.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Get a plugin-specific settings storage path.
    /// </summary>
    /// <param name="pluginId">The plugin's unique identifier.</param>
    /// <returns>Full path to the plugin's settings directory.</returns>
    string GetPluginDataPath(string pluginId);

    /// <summary>
    /// Register a custom service with the plugin system.
    /// </summary>
    void RegisterService<T>(T service) where T : class;

    /// <summary>
    /// Retrieve a registered service.
    /// </summary>
    T? GetService<T>() where T : class;

    /// <summary>
    /// Log a message to the X-Notch diagnostic log.
    /// </summary>
    void Log(string message, PluginLogLevel level = PluginLogLevel.Info);
}

public enum PluginLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
