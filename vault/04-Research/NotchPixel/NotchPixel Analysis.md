---
tags: [research, notchpixel, rea]
app: KOREINDUSTRIES.NotchPixel
version: 0.5.118.0
analysed: 2026-11-22
tool: REA (static, no execution)
---

# NotchPixel Analysis (v0.5.118)

Back to [[00 Home]] · earlier v0.5.113 report: [[REVERSE_ENGINEERING_REPORT]] · compare with [[FEATURE_COMPARISON]].

Installed at `C:\Program Files\WindowsApps\KOREINDUSTRIES.NotchPixel_0.5.118.0_x64__qgfctd1hmk5vm` (MSIX).

## Binary facts (REA `inspect_managed_artifact`)
| | `NotchPixel.dll` | `NotchPixel.Core.dll` |
|---|---|---|
| Size | 10.8 MB | 2.3 MB |
| Runtime | .NET 10, CIL + ReadyToRun, x64 | same |
| Types / methods | 1430 / 17 726 | 653 / 7 351 |
| Strong name | none | none |

Obfuscated: ~1110 types sit in the global namespace and in one-letter namespaces (`b`, `X`, `C`, `Z`, `n`, `f`). Only public enums keep real names, under `NotchPixel`, `.Scene`, `.Services`, `.Platform`, `.Taskbar`.

## Dependency stack (81 assembly refs)
- **Render**: SkiaSharp 4.152 + HarfBuzz, `SkiaSharp.Direct3D.Vortice`, Vortice D3D11/D3D12/DXGI/DirectComposition 3.8.3. Custom GPU scene, not WPF visuals.
- **Shell**: ManagedShell 0.0.374 (AppBar, WindowsTasks, WindowsTray, Interop)
- **Audio**: NAudio.Core + Wasapi. **Video capture**: Vortice.MediaFoundation
- **Web**: WebView2 1.0.4191 (web tiles), HttpListener + WebSockets + Sockets (phone "Deck" link), QRCoder (pairing QR)
- **Update**: Velopack 1.2
- **Other**: UIAutomationClient, System.Management (WMI), ProtectedData (DPAPI), Registry, FileSystem.Watcher

## State model (recovered enums)
| Enum | Values |
|---|---|
| `IslandMode` | Idle, Compact, Expanded, Slim, Hud, Overlay, Hidden |
| `IslandShape` | Island, Notch |
| `IslandPosition` | Center, Left, Right, LeftEdge, RightEdge |
| `AppBehaviour` | Normal, Slim, Hide, Game |
| `Occlusion` | Clear, Covered, Fullscreen |
| `LiveActivity` | None, Download, Export, Call, Recording, Agent |
| `CardPanel` | Media, Shelf, Clipboard, Widgets, Call, Tiles, Phone, Controls, Launcher, Notifications, Web, Chat, Agents, Deck |
| `HudKind` | Volume, Brightness, Charging, Unplugged, LowBattery, DeviceConnected/Disconnected, Notification, Welcome, TimerDone, Copied, MicMuted, MicLive, DownloadDone, Color, Reminder, LockKey |
| `NoticeGlyph` | Files, Phone, Message, Script, Media, Chart, Audio, Power, Colour, Reminder, Camera, Download, Web, Warning |
| `DeckState` | Off, Starting, On, NoNetwork, PublicNetwork, Failed |
| `ChatStatus` | Off, Invalid, Connecting, Live, NotLive, Failed (stream chat overlay) |
| `CameraState` | Off, Starting, Running, NoCamera, Denied, Failed |
| `PowerEvent` | PluggedIn, Unplugged, LowBattery, CriticalBattery |

`IslandHit` lists ~140 hit targets. Groups: media (PlayPause, Lyrics, AlbumBackground, MusicSource), shelf + phone transfer, clipboard (pin, snippets, filter), timer, quick controls (Wi-Fi, BT, Focus, NightLight, PowerMode), capture (Screenshot, Snip, ScreenRecord), call (join, test mic), Deck phone pairing, reminders + todo, AI agents (AgentReply, AgentStop, AgentTerminal), web tiles, trial/purchase.

## Gaps vs X-Notch
- X-Notch renders with WPF. NotchPixel renders with Skia on D3D + DirectComposition. Expect smoother animation at lower CPU cost.
- No X-Notch equivalent yet: phone Deck (LAN WebSocket + QR pairing), screen capture/record panel, call panel, stream chat, AI agent live activity, `Game` behaviour with occlusion detection.
- X-Notch already has a plugin SDK. NotchPixel shows no plugin surface.

## Limits
Static metadata only. No decompilation, execution, or network capture. Method bodies of obfuscated types not examined.
