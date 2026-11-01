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
  - `PluginBase`
  - `PluginManifest`

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
