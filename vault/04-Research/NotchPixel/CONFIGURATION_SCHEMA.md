# CONFIGURATION SCHEMA & PERSISTENCE SPECIFICATION
## Target Application: NotchPixel (v0.5.113.0)

---

### 1. Configuration Architecture

NotchPixel stores its user configuration in a single structured JSON document:
`%LOCALAPPDATA%\Packages\KOREINDUSTRIES.NotchPixel_qgfctd1hmk5vm\LocalCache\Roaming\NotchPixel\settings.json`

Persistence is hardened through two defensive mechanisms:
1. **Atomic Backup (`settings.json.bak`)**: Prior to writing updated settings to disk, the existing file is copied to `.bak` to protect against write corruption during unexpected power cuts or crashes.
2. **Surface Safety State (`surface-safety.json`)**: Tracks surface rendering trials and crashes. If a newly selected display surface or GPU setting causes an immediate crash upon launch, the safety manager reverts the setting to the previous known-good configuration.

---

### 2. Complete Configuration Key Reference

Below is the verified schema recovered directly from active installations:

#### 2.1 Geometry & Display Positioning
| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Position` | string | `"Center"` | Horizontal placement across monitor edge (`"Center"`, `"Left"`, `"Right"`). |
| `Shape` | string | `"Island"` | Visual silhouette (`"Island"`, `"Notch"`, `"FloatingPill"`). |
| `CornerRoundness`| integer| `100` | Corner radius percentage (0 = sharp rectangle, 100 = full capsule). |
| `Size` | integer| `100` | Scale multiplier for idle pill width/height (50 - 200%). |
| `OpenSize` | integer| `100` | Scale multiplier for expanded card size. |
| `OpenScale` | float | `1.0` | Global DPI scaling override factor. |
| `TopGap` / `TopGapDip` | integer| `6` | Offset in DIPs from top monitor bezel (0 = flush). |
| `SidePosition` | integer| `50` | Center position percentage across screen width. |
| `AllMonitors` | boolean| `false` | When true, renders an island instance on every active monitor. |
| `Monitor` | string | `""` | Target monitor device ID (empty = primary monitor). |

#### 2.2 Interaction & Window Behaviors
| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `SlimWhenCovered`| boolean| `true` | Collapses to an 8px line when a maximized window covers the island. |
| `HideInFullscreen`| boolean| `false` | Hides island completely when an application is in exclusive fullscreen. |
| `HiddenUntilHover`| boolean| `false` | Keeps island 100% invisible until cursor touches top edge. |
| `HoverDelayMs` | integer| `150` | Milliseconds cursor must remain over island before card expands. |
| `ReverseIslandScroll`| boolean| `false` | Inverts mouse wheel scroll direction for volume/brightness adjustment. |

#### 2.3 System HUD Flyout Overrides
| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ReplaceVolumeFlyout`| boolean| `true` | Suppresses Windows 11 default volume flyout and renders notch HUD. |
| `ShowVolumeHud` | boolean| `true` | Displays volume slider capsule on volume key presses. |
| `ShowBrightnessHud`| boolean| `true` | Displays brightness capsule on monitor brightness adjustments. |
| `ShowBatteryHud` | boolean| `true` | Shows notification when battery state changes (charging, low battery). |
| `ShowBluetoothHud` | boolean| `true` | Shows capsule when Bluetooth audio device connects/disconnects. |
| `ShowLockKeysHud` | boolean| `true` | Shows indicator for CapsLock, NumLock, and ScrollLock state changes. |
| `ShowMicHud` | boolean| `true` | Visual feedback when microphone mute state toggles. |
| `ShowPrivacyIndicator`| boolean| `true` | Top-right dot indicator when webcam or microphone is accessed by any app. |

#### 2.4 Media & Visualizer
| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Visualizer` | string | `"pixels"` | Audio visualization mode (`"pixels"`, `"bars"`, `"wave"`, `"none"`). |
| `MusicBackground`| string | `"album"` | Expanded media card backdrop (`"album"`, `"glass"`, `"solid"`). |
| `ProgressBar` | string | `"wave"` | Track timeline style (`"wave"`, `"flat"`, `"none"`). |
| `ShowLyrics` | boolean| `false` | Enables real-time synchronized lyrics panel. |
| `MediaSourceMode`| string | `"all"` | Media session filtering (`"all"` vs `"whitelist"`). |

#### 2.5 Productivity, Shelf & Clipboard
| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ShelfEnabled` | boolean| `true` | Enables drag-and-drop file staging shelf. |
| `CapturesToShelf`| boolean| `true` | Automatically drops screenshots and snips into the shelf. |
| `ClipboardEnabled`| boolean| `true` | Enables clipboard history tracking. |
| `ClipboardHistorySize`| integer| `12` | Maximum items retained in clipboard ring buffer (up to 50). |
| `ClipboardKeepHours`| integer| `10` | TTL in hours before non-pinned clipboard items are pruned. |
| `PhoneShare` | boolean| `true` | Enables local Wi-Fi file & clipboard sharing with companion phone app. |
| `NotifyEndpoint` | boolean| `true` | Binds local HTTP listener on port 47823. |
| `NotifyPort` | integer| `47823` | TCP port for local webhook and phone notifications. |

#### 2.6 Widgets & Utilities
| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ShowDateWidget` | boolean| `true` | Calendar date pill. |
| `ShowTimerWidget`| boolean| `true` | Focus / stopwatch timer pill. |
| `ShowWeatherWidget`| boolean| `true` | Current weather condition and temperature. |
| `ShowStatsWidget`| boolean| `true` | Mini CPU/RAM system statistics readout. |
| `FocusMinutes` | integer| `25` | Pomodoro focus duration in minutes. |
| `BreakMinutes` | integer| `5` | Pomodoro short break duration in minutes. |
| `PingHost` | string | `"1.1.1.1"` | Target ICMP / DNS host for network latency monitor. |

#### 2.7 Gamer Overlay Strip
| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `GamerOverlay` | boolean| `true` | Enables top horizontal performance overlay strip. |
| `OverlayFps` | boolean| `true` | Shows live frames per second (requires Performance Log Users). |
| `OverlayCpu` | boolean| `true` | Displays CPU utilization percentage and temperature. |
| `OverlayGpu` | boolean| `true` | Displays GPU utilization percentage and VRAM usage. |
| `OverlayRam` | boolean| `true` | Displays active system RAM usage. |
| `OverlayNetwork` | boolean| `true` | Displays download / upload throughput. |
| `ObsEnabled` | boolean| `false` | Enables OBS Studio WebSocket integration. |
| `ObsPort` | integer| `4455` | OBS WebSocket v5 port. |

#### 2.8 Global Hotkey Mapping (`Hotkeys`)
Default key gestures mapped via `RegisterHotKey`:

```json
[
  { "Action": "toggle-card",   "Gesture": "Ctrl+Alt+N" },
  { "Action": "play-pause",     "Gesture": "Ctrl+Alt+P" },
  { "Action": "timer",          "Gesture": "Ctrl+Alt+T" },
  { "Action": "hide",           "Gesture": "Ctrl+Alt+H" },
  { "Action": "mic-mute",       "Gesture": "Ctrl+Alt+M" },
  { "Action": "obs-record",     "Gesture": "Ctrl+Alt+R" },
  { "Action": "output",         "Gesture": "Ctrl+Alt+O" },
  { "Action": "color-pick",     "Gesture": "Ctrl+Alt+C" },
  { "Action": "screenshot",     "Gesture": "Ctrl+Alt+S" },
  { "Action": "screen-record",  "Gesture": "Ctrl+Alt+Shift+R" },
  { "Action": "chat-drawer",    "Gesture": "Ctrl+Alt+Shift+C" }
]
```

---

### 3. Surface Safety Rollback (`surface-safety.json`)

To prevent crash loops caused by bad GPU drivers or incompatible display surface configurations, NotchPixel maintains a safety ledger:

```json
{
  "TrialSurface": "",
  "TrialStartedAt": null,
  "RunningSince": "2026-11-17T02:07:42",
  "LastAliveAt": "2026-11-17T02:50:00",
  "Crashes": [],
  "RevertedFrom": ""
}
```

If the process terminates abnormally within 30 seconds of altering a display setting (`TrialSurface`), the startup watchdog automatically rolls back the configuration to the previous known-good state.
