# X-Notch

X-Notch is a Dynamic Island-inspired notch overlay for Windows by Gravit. It brings media controls, calendar, battery, webcam, volume and brightness HUDs, file staging, and installable plugins into a compact surface that can live at the top or side of the screen.

Built with `WPF`, `.NET 8`, and `C#`.

![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)

## Copyright

**Copyright © Gravit. All rights reserved.**

## Features

- Dynamic notch with hover, click, drag, and scroll interactions
- Multi-monitor support: drag the island to any screen and dock it there; optional island on every screen with synchronized (or independent) open state
- Built-in media controls, audio visualizer, and lyrics sync
- Volume and brightness HUDs
- Full Calendar with Agenda, Tasks & Reminders, Battery indicator, File Shelf, and Webcam mirror
- Light, dark, and auto theme support
- Position-aware layouts, including vertical plugin surfaces
- Plugin Manager and Plugin Browser with online library support
- Configurable plugins with built-in setup UI

## Install

### Release build

1. Download the latest installer from Releases.
2. Run `X-Notch-Setup.exe`.
3. Launch X-Notch.

### Build from source

Requirements:

- Windows 10 or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
dotnet run --project src/XNotch/XNotch.csproj
```

To create a publish folder:

```bash
dotnet publish src/XNotch/XNotch.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Plugins

X-Notch supports several plugin types:

- `IUIPlugin`
- `IServicePlugin`
- `IAnimationPlugin`
- `IConfigurablePlugin`

Plugins can be installed in two ways:

1. From the in-app Plugin Browser.
2. Manually by placing a plugin DLL in `%AppData%\X-Notch\Plugins\<plugin-id>\`.

## License

GPL-3.0 • Copyright © Gravit. All rights reserved.
