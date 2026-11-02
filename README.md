<div align="center">

# X-Notch

### *The Intelligent, Fluid Dynamic Island for Windows*

[![GitHub Release](https://img.shields.io/github/v/release/hellogravitagency-maker/X-Notch?include_prereleases&style=for-the-badge&color=0078D4)](https://github.com/hellogravitagency-maker/X-Notch/releases)
[![Build Status](https://img.shields.io/github/actions/workflow/status/hellogravitagency-maker/X-Notch/ci.yml?branch=main&style=for-the-badge&label=CI%2FCD)](https://github.com/hellogravitagency-maker/X-Notch/actions)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?style=for-the-badge&logo=windows)](https://github.com/hellogravitagency-maker/X-Notch)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0%20(WPF)-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![DPI Awareness](https://img.shields.io/badge/DPI-Per--Monitor%20V2-success?style=for-the-badge)](https://learn.microsoft.com/en-us/windows/win32/hidpi/dpi-awareness-context)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg?style=for-the-badge)](LICENSE)

<br/>

**X-Notch** brings Apple-inspired Dynamic Island elegance and fluid physics to Windows 10 and 11. Built entirely with high-performance WPF and .NET 8, X-Notch consolidates media playback, system vitals, productivity widgets, notifications, hardware HUDs, and an extensible plugin ecosystem into a non-intrusive, adaptive desktop overlay.

---

</div>

## 📑 Table of Contents

- [✨ Key Features](#-key-features)
- [📦 Installation & Downloads](#-installation--downloads)
- [🔄 Auto-Updates & Upgrades](#-auto-updates--upgrades)
- [🧩 Plugin Ecosystem & Management](#-plugin-ecosystem--management)
  - [Managing Plugins](#managing-plugins)
  - [Plugin SDK & Creating Plugins](#plugin-sdk--creating-plugins)
- [🚀 Continuous Integration & Delivery (CI/CD)](#-continuous-integration--delivery-cicd)
- [⌨️ Shortcuts & Gestures](#️-shortcuts--gestures)
- [🛠️ Building from Source](#️-building-from-source)
- [📄 License & Copyright](#-license--copyright)

---

## ✨ Key Features

### 🌊 Fluid Physics & Dynamic Geometry
- **Damped Spring Physics:** Smooth, organic Apple-style spring animations for expanding, contracting, and peeking states.
- **Continuous G2 Curvature:** Native continuous corner curvature calculated in real time.
- **Ambient Desktop Glow (Aura):** Optional ambient lighting effect that smoothly pulses with active notifications and media accents.

### 🖥️ Multi-Monitor & Docking Flexibility
- **Flexible Docking:** Dock at the **Top Center**, or snap vertically along the **Left** or **Right** screen edges.
- **Multi-Monitor Islands:** Drag and drop the island between monitors, or enable individual islands on every connected display with synchronized or independent states.
- **Per-Monitor V2 DPI Awareness:** Sharp, pixel-perfect rendering across mixed-scale displays (e.g. 4K 150% laptop + 1080p 100% external monitor) without DWM bitmap blur.

### 🎵 Media Controller & Audio Visualizer
- **System Media Integration:** Full Windows SMTC media transport synchronization (Spotify, Apple Music, YouTube, Edge, Chrome).
- **Real-Time Audio Visualizer:** Low-latency WASAPI loopback audio spectrum analysis with customizable sensitivity.
- **Quick Controls:** Middle-click closed notch to toggle play/pause; scroll over the notch to adjust system volume; click open to reveal interactive scrubbing, track elapsed/remaining time, and high-res album artwork.

### 📊 Productivity & Widget Carousel
- **Tasks & Focus:** Integrated task manager with automatic code-comment task discovery (`TODO`, `FIXME`) and quick keyboard entry.
- **System Vitals:** Live hardware monitoring for CPU utilization, RAM usage, GPU temperature, battery health, and ASUS Armoury Crate / Performance operating mode switching (*Silent*, *Performance*, *Turbo*).
- **Wellness Trackers:** Built-in Water Tracker and Sleep Tracker widgets.
- **Notification History:** Review recent Windows desktop notifications right inside the dynamic island.

### 🧰 Integrated Utilities & Privacy
- **Spotlight Search Launcher (`Ctrl+Space`):** Quick application launcher and document search directly from the island.
- **File Shelf:** Drag-and-drop temporary staging capsule for files, documents, and clipboard items.
- **Hardware Privacy Indicators:** Live green/amber indicator dots displaying when apps access the Webcam or Microphone.
- **Webcam Mirror:** Instant camera preview popover for checking video appearance before calls.
- **Volume & Brightness HUD:** Modern on-screen liquid slider notifications replacing the default Windows overlay.

### 🎨 Clean, Standard Themes
- **Dark Theme:** Deep neutral slate palette designed for low eye strain and high readability.
- **Light Theme:** Clean, crisp daylight palette with balanced perceptual contrast.
- **Liquid Glass:** Frosted glass effect with real-time desktop wallpaper color extraction and acrylic blur.
- **Windows Adaptation:** Automatically synchronizes island appearance with Windows light/dark system mode.

---

## 📦 Installation & Downloads

Pre-compiled production binaries are available on the [**GitHub Releases**](https://github.com/hellogravitagency-maker/X-Notch/releases) page.

| Package Type | File | Description |
| :--- | :--- | :--- |
| **Standalone Portable** | [`X-Notch.exe`](https://github.com/hellogravitagency-maker/X-Notch/releases/latest) | Single-file, self-contained executable. No installation or prerequisites needed. Download and run anywhere. |
| **Windows Installer** | [`X-Notch-Setup.exe`](https://github.com/hellogravitagency-maker/X-Notch/releases/latest) | Complete Inno Setup installer with automatic startup registration, Start Menu shortcuts, and seamless silent background auto-updates. |
| **Official Plugins** | [`X-Notch-Plugins.zip`](https://github.com/hellogravitagency-maker/X-Notch/releases/latest) | Official example plugins bundle ready for installation. |
| **Integrity Checksums** | [`SHA256SUMS.txt`](https://github.com/hellogravitagency-maker/X-Notch/releases/latest) | Cryptographic SHA-256 hashes to verify release package integrity. |

---

## 🔄 Auto-Updates & Upgrades

X-Notch features an integrated, silent self-update engine powered by [`UpdateService.cs`](src/XNotch/Services/UpdateService.cs):

```
┌────────────────────────┐      Polls GitHub API       ┌──────────────────────────────────────┐
│  X-Notch Client        │ ──────────────────────────> │ github.com/.../releases/latest       │
│  (Background / Manual) │                             └──────────────────────────────────────┘
└────────────────────────┘                                                │
           │                                                              ▼
           │  Download & Verify Checksum                ┌──────────────────────────────────────┐
           ├──────────────────────────────────────────  │ X-Notch-<Version>-Setup.exe          │
           ▼                                            └──────────────────────────────────────┘
┌────────────────────────┐      Silent Launch (/UPDATE=1)
│  In-Place Upgrade      │ ──────────────────────────────────> Seamless restart into new version
└────────────────────────┘
```

1. **Automatic Background Check:** On startup (and periodically if enabled in Settings), X-Notch queries the latest GitHub Release API.
2. **Safe Download:** When a newer release is published, the installer is securely fetched into `%LOCALAPPDATA%\X-Notch\Updates\`.
3. **Silent In-Place Upgrade:** The updater runs the installer with `/UPDATE=1`, smoothly replacing the binaries, releasing mutexes, and relaunching the updated application without interrupting your settings.
4. **Manual Trigger:** You can manually check for and apply updates anytime via **⚙ Settings → Check for Updates**.

---

## 🧩 Plugin Ecosystem & Management

X-Notch provides a dynamic plugin system allowing developers to build custom notch widgets, background services, custom easing animations, and configuration pages.

### Managing Plugins

Open the in-notch settings (**⚙ Settings → Manage Plugins**) to launch the **Plugin Browser & Manager**:

- **Live Toggle:** Enable or disable plugins on the fly with immediate UI injection (no restart required).
- **Online Catalog:** Browse, search, and install verified community plugins directly from the in-app library.
- **Configurable Settings:** Click **Configure** on any supporting plugin to adjust parameters, timers, and layout styles.
- **Manual Installation:** Drop any plugin directory into `%APPDATA%\X-Notch\Plugins\<PluginId>\`.

### Plugin SDK & Creating Plugins

Plugins are compiled against the official [`XNotch.PluginSdk`](src/XNotch.PluginSdk/) package.

#### 1. Add Reference to the Plugin SDK
```xml
<ItemGroup>
  <PackageReference Include="XNotch.PluginSdk" Version="0.8.0" />
</ItemGroup>
```

#### 2. Implement the Plugin Interface
```csharp
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using XNotch.Models;
using XNotch.Plugins;

namespace MyCustomPlugin;

[XNotchPlugin("com.example.customplugin", "My Custom Plugin", "1.0.0", "Your Name")]
public class CustomPlugin : PluginBase, IUIPlugin
{
    public override string Id => "com.example.customplugin";
    public override string Name => "My Custom Plugin";
    public override string Version => "1.0.0";
    public override string Description => "Custom dynamic island widget.";

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.OpenContent => new TextBlock
            {
                Text = "Hello from Custom Plugin!",
                FontSize = 12,
                Margin = new Thickness(8)
            },
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState) { }
}
```

---

## 🚀 Continuous Integration & Delivery (CI/CD)

The repository uses automated GitHub Actions workflows to guarantee code quality, build verification, automated release generation, and plugin validation:

| Workflow | Trigger | Action & Output |
| :--- | :--- | :--- |
| [**CI**](.github/workflows/ci.yml) | Push & Pull Request on `main` | Restores, builds solution in Release mode, runs **all 84 unit/integration tests**, and verifies single-file publish output. |
| [**Release**](.github/workflows/release.yml) | Push tag `v*` or manual dispatch | Compiles self-contained portable `X-Notch.exe`, builds Inno Setup `*-Setup.exe`, packages official plugins, computes SHA256 checksums, and publishes the GitHub Release with automated release notes. |
| [**Manage Plugins**](.github/workflows/manage-plugins.yml) | Changes in `Examples/Plugins/**` | Validates `example-library.json` catalog integrity, compiles all plugin projects, packages plugins into individual zip distributions, and uploads distribution artifacts. |
| [**Publish Plugin SDK**](.github/workflows/publish-plugin-sdk.yml) | Release published or manual dispatch | Packages `XNotch.PluginSdk` NuGet package and publishes it to GitHub Packages. |

---

## ⌨️ Shortcuts & Gestures

| Action | Shortcut / Gesture | Behavior |
| :--- | :--- | :--- |
| **Toggle Island** | `Ctrl + Alt + N` | Toggles the Dynamic Island between open and closed states. |
| **Spotlight Launcher** | `Ctrl + Space` | Opens the Spotlight quick search and application launcher. |
| **Play / Pause** | **Middle-Click** (Closed Notch) | Toggles active media playback without expanding the island. |
| **Volume Adjustment** | **Mouse Wheel** (Over Notch) | Increases or decreases system volume with animated HUD feedback. |
| **Carousel Navigation** | **Scroll** or **Click Arrows** | Cycles through open notch cards (Tasks, Vitals, Water, Sleep, Notifications). |
| **Move / Dock Island** | **Click & Drag Pill** | Drag the island to snap to Top, Left, or Right dock positions, or across multiple monitors. |
| **Close Overlay** | **Click Away** or `Escape` | Gracefully closes the expanded island back to its resting state. |

---

## 🛠️ Building from Source

### Prerequisites
- Windows 10 (Build 17763+) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 / Rider / VS Code (with C# Dev Kit)

### 1. Clone the Repository
```bash
git clone https://github.com/hellogravitagency-maker/X-Notch.git
cd X-Notch
```

### 2. Build Solution
```bash
dotnet build X-Notch.sln -c Release
```

### 3. Run Test Suite
```bash
dotnet test tests/XNotch.Tests/XNotch.Tests.csproj -c Release
```

### 4. Publish Single-File Executable
```bash
dotnet publish src/XNotch/XNotch.csproj -c Release -o publish -p:PublishSingleFile=true
```
The compiled standalone executable will be generated at `publish\X-Notch.exe`.

---

## 📄 License & Copyright

Distributed under the **GNU General Public License v3.0** (GPL-3.0). See [LICENSE](LICENSE) for more details.

**Copyright © Gravit. All rights reserved.**
