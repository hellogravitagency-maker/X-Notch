# X-Notch Plugin SDK

`XNotch.PluginSdk` is the compile-time package for building X-Notch plugins against the official X-Notch assembly.

## What it contains

- The real reference assembly used by the app
- XML documentation for the plugin-facing APIs when available
- The public plugin contracts such as:
  - `IPlugin`
  - `IUIPlugin`
  - `IServicePlugin`
  - `IAnimationPlugin`
  - `IConfigurablePlugin`
  - `ILiveActivityPlugin` and `IIslandAlerts` (Dynamic Island, 0.9.0+)
  - `PluginBase`
  - `PluginManifest`

## Dynamic Island (0.9.0+)

Additive APIs. Every earlier interface works unchanged.

- `ILiveActivityPlugin.GetLiveActivities()` returns `XNotch.Island.Activities.ILiveActivity` instances. Each one supplies:
  - `Id`
  - `Priority`, clamped to 0–80 (default 40). Built-ins: agent 90, timer 70, stopwatch 60, media 50, shelf 30.
  - `StartedAt` and `IsActive`, plus a `Changed` event
  - view factories: `CreateCompactLeading` (22 px slot), `CreateCompactTrailing`, `CreateMinimalView` (32 px circle) and `CreateExpandedView` (a carousel page, optional)
- `Context.GetService<IIslandAlerts>()?.Post(title, subtitle, icon, key)` shows a 2.6 s island alert. In Classic mode it shows a regular notification instead.
- `XNotch.Island.Activities.IslandVisuals`, `ProgressRing`, `WaveformBars` and `XNotch.Controls.RollingNumberText` give plugin views the island look.

The Focus Timer example (`Examples/Plugins/FocusTimerPlugin`) is the reference implementation. A running session shows as a compact ring plus rolling time.

```csharp
public sealed class MyPlugin : PluginBase, ILiveActivityPlugin
{
    public IEnumerable<ILiveActivity> GetLiveActivities() { yield return _activity; }
}
```

## Install from GitHub Packages

First add the GitHub Packages NuGet source:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/Gravit/index.json" \
  --name github-xnotch \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text
```

Your personal access token needs `read:packages`.

Then add the package:

```bash
dotnet add package XNotch.PluginSdk --version 0.7.0 --source github-xnotch
```

## Important note

This package is for plugin development. End users should still install X-Notch itself from GitHub Releases.

At runtime, your plugin loads inside the X-Notch app and uses the app's assembly. Keep your plugin's `MinimumXNotchVersion` aligned with the app version you target.
