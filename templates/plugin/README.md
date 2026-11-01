# X-Notch Plugin Template

Quick-start template for building X-Notch plugins.

## Setup

1. Copy this folder to your desired location
2. Rename the project (`.csproj`, namespace, class name)
3. Update the `ProjectReference` path in the `.csproj` to point to your local X-Notch source
4. Update the plugin metadata (`Id`, `Name`, `Author`, etc.)

## Build

```bash
dotnet build -c Release
```

## Install

Copy the output DLL from `bin/Release/net8.0-windows/` to:

```
%AppData%\X-Notch\Plugins\YourPluginName\
```

Restart X-Notch. Your plugin will be auto-discovered and enabled.

## Plugin Types

| Interface | Purpose |
|-----------|---------|
| `IPlugin` | Base — lifecycle hooks only |
| `IUIPlugin` | Inject UI into notch at various locations |
| `IServicePlugin` | Run background tasks |
| `IAnimationPlugin` | Override notch expand/collapse/peek animations |

## UIPluginLocation

| Location | Description |
|----------|-------------|
| `ClosedContent` | Compact notch bar (alongside clock/battery) |
| `OpenContent` | Expanded notch main area |
| `CustomTab` | Additional tab in expanded view |
| `Settings` | Plugin settings panel |
| `Overlay` | Highest-priority overlay layer |

## Available Services via IPluginContext

- `MediaService` — Now playing info, playback control
- `AudioCaptureService` — Audio spectrum data (12 bands)
- `VolumeService` / `BrightnessService` — System volume/brightness
- `BatteryService` — Battery status
- `CalendarService` — Calendar events
- `ThemeService` — Current theme, theme changes
- `ShelfService` — File shelf management
- `WebcamService` — Webcam stream
- `FullscreenService` — Fullscreen app detection
- `NotchViewModel` — Current notch state
- `AppSettings` — User settings

## Data Storage

```csharp
var dataPath = Context.GetPluginDataPath(Id);
// Returns: %AppData%\X-Notch\Plugins\{pluginId}\
```

## Logging

```csharp
Context.Log("Something happened", PluginLogLevel.Info);
// Writes to: %AppData%\X-Notch\plugin.log
```

See [PLUGIN_DEVELOPMENT.md](../../PLUGIN_DEVELOPMENT.md) for the full guide.
