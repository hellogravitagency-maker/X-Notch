# X-Notch — Project Plan

## 1. What is Boring Notch (Reference Project)

**Boring Notch** is a macOS app (8.4k stars, SwiftUI/Swift) that transforms the MacBook's hardware notch into a **Dynamic Island**-style interactive overlay. It sits at the top-center of the screen and provides:

- **Compact state (closed)**: A small pill-shaped black bar (mimicking the hardware notch) that shows live music activity (album art + audio visualizer), battery notifications, volume/brightness HUD, or an idle animated face.
- **Expanded state (open)**: On hover/click/gesture, it expands with a spring animation to reveal full music controls, calendar, webcam mirror, and more.

### Core Features to Replicate

| Feature | Description |
|---|---|
| **Notch Shape** | Custom rounded-bottom rectangle with quad-curve corners, black background, shadow on hover |
| **Music Controls** | Album art, song title (marquee), artist, play/pause/next/prev/shuffle/repeat, progress slider, volume, lyrics, audio spectrum visualizer |
| **Music Live Activity** | Compact mode shows album art + visualizer + song title when music is playing |
| **Calendar** | Upcoming events panel in expanded view |
| **Battery Indicator** | Charging status, percentage, low power mode — shown as compact notification |
| **System HUD Replacement** | Volume and brightness changes shown as inline sliders inside the notch instead of default Windows overlay |
| **File Shelf** | Drag & drop files onto the notch for quick access/sharing |
| **Webcam Mirror** | Live camera preview in expanded view |
| **Animations** | Spring physics (open/close), gesture-driven scaling, matched geometry transitions, marquee text, Lottie/animated face |
| **Settings** | System tray icon → settings window for customization |
| **Gestures** | Hover to open, swipe down to open, swipe up to close, click to open |
| **Color Theming** | Extract dominant color from album art → tint UI elements |
| **Sneak Peek** | Brief notifications (volume change, brightness, song change) shown inline without fully opening |

---

## 2. Technology Stack

| Layer | Technology | Rationale |
|---|---|---|
| **Framework** | WPF (.NET 8, C#) | Mature transparent-window support, hardware-accelerated rendering, rich animation system |
| **Window** | `WindowStyle=None` + `AllowsTransparency=True` + `Topmost=True` | Borderless transparent overlay |
| **Rendering** | WPF Composition + custom `Shape` / `Path` | Notch shape with animated corner radii |
| **Animations** | WPF `Storyboard` + custom spring easing + `CompositionTarget.Rendering` | 60fps spring physics matching boring.notch |
| **Media** | Windows.Media.Control (`GlobalSystemMediaTransportControlsSession`) | Now Playing info from any app (Spotify, browser, etc.) |
| **Audio Visualizer** | NAudio (WASAPI loopback capture) | Real-time audio spectrum for the visualizer bars |
| **Calendar** | Microsoft Graph API or Windows Calendar COM | Upcoming events |
| **Battery** | `System.Windows.Forms.PowerStatus` or WMI | Charge level, charging state |
| **Volume/Brightness** | NAudio (audio endpoint) + WMI/DDC for brightness | System HUD replacement |
| **Webcam** | OpenCvSharp or MediaCapture (WinRT) | Live camera preview |
| **Settings Storage** | `System.Text.Json` + local JSON file | User preferences |
| **Tray Icon** | `System.Windows.Forms.NotifyIcon` or Hardcodet.NotifyIcon.Wpf | System tray menu |
| **Color Extraction** | Custom algorithm or ColorThief.NET | Dominant color from album art |
| **Drag & Drop** | WPF built-in `DragDrop` | File shelf feature |
| **Installer** | MSIX or Inno Setup | Distribution |

---

## 3. Architecture

```
X-Notch/
├── src/
│   └── X-Notch/                          # Main WPF application
│       ├── X-Notch.csproj
│       ├── App.xaml / App.xaml.cs          # Application entry, tray icon, single instance
│       ├── Models/
│       │   ├── NotchState.cs              # Enum: Closed, Open
│       │   ├── NotchViewModel.cs          # Main ViewModel (notch size, state, open/close)
│       │   ├── MediaInfo.cs               # Song title, artist, album art, duration, position
│       │   ├── BatteryInfo.cs             # Level, charging, power saver
│       │   ├── CalendarEvent.cs           # Event title, time, calendar color
│       │   └── ShelfItem.cs              # Dropped file path, icon, name
│       ├── Views/
│       │   ├── NotchWindow.xaml/cs        # Main transparent topmost window
│       │   ├── NotchShape.cs              # Custom WPF Shape (the notch outline)
│       │   ├── NotchClosedView.xaml       # Compact state content
│       │   ├── NotchOpenView.xaml         # Expanded state content
│       │   ├── MusicPlayerView.xaml       # Album art, controls, slider, lyrics
│       │   ├── MusicLiveActivity.xaml     # Compact music indicator (album art + visualizer)
│       │   ├── AudioVisualizerView.xaml   # Spectrum bars
│       │   ├── CalendarView.xaml          # Upcoming events list
│       │   ├── BatteryView.xaml           # Battery indicator
│       │   ├── HudOverlay.xaml            # Volume/brightness inline slider
│       │   ├── ShelfView.xaml             # File shelf grid
│       │   ├── WebcamView.xaml            # Camera preview
│       │   ├── SettingsWindow.xaml        # Settings panel
│       │   └── Components/
│       │       ├── MarqueeText.xaml       # Auto-scrolling text
│       │       ├── SpringAnimation.cs     # Spring physics helper
│       │       ├── HoverButton.xaml       # Animated hover button
│       │       └── CustomSlider.xaml      # Styled progress/volume slider
│       ├── Services/
│       │   ├── MediaService.cs            # SMTC session monitoring (Now Playing)
│       │   ├── AudioCaptureService.cs     # WASAPI loopback for visualizer
│       │   ├── BatteryService.cs          # Battery monitoring
│       │   ├── BrightnessService.cs       # Monitor brightness control
│       │   ├── VolumeService.cs           # System volume get/set
│       │   ├── CalendarService.cs         # Calendar events fetching
│       │   ├── WebcamService.cs           # Camera capture
│       │   ├── ColorExtractionService.cs  # Dominant color from album art
│       │   ├── ShelfService.cs            # File shelf management
│       │   └── HotkeyService.cs           # Global keyboard shortcuts
│       ├── Helpers/
│       │   ├── ScreenHelper.cs            # Screen geometry, DPI, notch positioning
│       │   ├── WindowHelper.cs            # Win32 interop (click-through, always-on-top)
│       │   └── AnimationHelper.cs         # Easing functions, spring math
│       └── Assets/
│           ├── Fonts/
│           ├── Icons/
│           └── Animations/                # Lottie JSON files
├── tests/
│   └── X-Notch.Tests/
│       └── X-Notch.Tests.csproj
├── .gitignore
├── README.md
├── LICENSE
├── PLAN.md                                # This file
└── X-Notch.sln
```

---

## 4. Implementation Phases

### Phase 1 — Core Window & Notch Shape
**Goal**: Black notch-shaped overlay at top-center of screen, open/close states.

- [ ] Create WPF project (.NET 8, `net8.0-windows`)
- [ ] `NotchWindow`: transparent, borderless, topmost, positioned at top-center of primary screen
- [ ] `NotchShape`: custom `Shape` class drawing the notch outline (quad bezier curves matching boring.notch's `NotchShape.swift`)
- [ ] `NotchViewModel`: `INotifyPropertyChanged`, `NotchState` (Open/Closed), notch size, open/close methods
- [ ] Spring animation system: custom spring easing for width/height transitions (response ~0.4s, damping ~0.8)
- [ ] Hover detection: expand on mouse enter, collapse on mouse leave (with debounce)
- [ ] Click to open, right-click context menu
- [ ] Shadow on hover/open
- [ ] Win32 interop: `WS_EX_TOOLWINDOW` (hide from taskbar/alt-tab), `WS_EX_NOACTIVATE`
- [ ] DPI-awareness and multi-monitor support (position on correct screen)

### Phase 2 — Music Controls (Main Feature)
**Goal**: Full music player in expanded notch + compact live activity.

- [ ] `MediaService`: connect to Windows SMTC (`GlobalSystemMediaTransportControlsSessionManager`)
  - Get current session (Spotify, browser, etc.)
  - Read: song title, artist, album art (thumbnail), playback status, timeline position, duration
  - Control: play/pause, next, previous, shuffle, repeat
- [ ] `MusicPlayerView` (expanded):
  - Album art with rounded corners + lighting effect (blurred background glow)
  - Song title + artist as MarqueeText (scrolling if too long)
  - Progress slider (draggable, shows elapsed/total time)
  - Control buttons: shuffle, prev, play/pause, next, repeat
  - Volume control (slider popup)
- [ ] `MusicLiveActivity` (compact/closed):
  - Small album art thumbnail on left
  - Audio visualizer bars on right
  - Smooth transition between compact ↔ expanded (matched element animation)
- [ ] `AudioVisualizerView`:
  - NAudio WASAPI loopback capture
  - FFT → frequency bands → animated bars
  - Color tinted from album art
- [ ] `ColorExtractionService`: extract dominant color from album art bitmap
  - Tint visualizer, slider, artist text, glow effect
- [ ] MarqueeText control: auto-scroll text that overflows

### Phase 3 — System HUD Replacement
**Goal**: Volume/brightness changes appear as inline sliders in the notch.

- [ ] `VolumeService`: monitor system volume changes (NAudio `MMDeviceEnumerator`)
  - Detect volume change events
  - Get/set volume level
- [ ] `BrightnessService`: monitor/set display brightness (WMI `WmiMonitorBrightness` or DDC/CI)
- [ ] `HudOverlay` (sneak peek): when volume/brightness changes, show a compact inline slider in the closed notch
  - Auto-dismiss after ~2 seconds
  - Draggable to adjust
  - Suppress default Windows volume overlay (optional, via registering as volume OSD)

### Phase 4 — Battery Status
**Goal**: Battery level indicator + charging notification.

- [ ] `BatteryService`: poll `SystemInformation.PowerStatus` or WMI
  - Battery percentage, charging state, power saver mode
- [ ] `BatteryView`: compact battery icon with level fill + percentage text
- [ ] Charging notification: when plugged in/unplugged, show a brief expanding notification in the closed notch (like boring.notch's battery live activity)

### Phase 5 — Calendar Integration
**Goal**: Show upcoming events in the expanded notch.

- [ ] `CalendarService`: fetch events from Windows Calendar
  - Option A: Microsoft Graph API (Outlook/Microsoft 365)
  - Option B: Read local `.ics` files or Windows Calendar data
  - Show today's + upcoming events
- [ ] `CalendarView`: scrollable list of events with time, title, color indicator
  - Shown alongside music player in expanded view
  - Hover interaction for details

### Phase 6 — File Shelf
**Goal**: Drag & drop files onto the notch for temporary storage.

- [ ] `ShelfService`: manage list of `ShelfItem` (file path, display name, icon)
- [ ] `ShelfView`: grid/list of dropped files with icons
  - Drag files onto the closed notch → auto-open to shelf tab
  - Click file to open, drag out to move
  - Right-click for context menu (open, copy path, remove)
- [ ] Tab system: switch between Home (music) and Shelf views in expanded state
- [ ] Drop zone detection on closed notch

### Phase 7 — Webcam Mirror
**Goal**: Live camera preview in expanded notch.

- [ ] `WebcamService`: capture from default camera (MediaCapture WinRT or OpenCvSharp)
- [ ] `WebcamView`: live preview with rounded corners
  - Toggle on/off from settings or header button
  - Shown alongside music + calendar in expanded view
  - Handle camera permissions gracefully

### Phase 8 — Settings & System Tray
**Goal**: Full settings panel + tray icon.

- [ ] System tray icon with context menu (Settings, Quit)
- [ ] `SettingsWindow`:
  - **General**: start on boot, open on hover (enable/disable), hover delay, monitor selection
  - **Appearance**: corner radius scaling, shadow, color tinting, visualizer style
  - **Music**: show lyrics, visualizer type, control button layout
  - **HUD**: enable volume/brightness HUD, inline style
  - **Calendar**: enable/disable, account connection
  - **Shelf**: enable/disable, open shelf by default
  - **Webcam**: enable/disable, camera selection
  - **Gestures**: enable/disable, sensitivity
  - **About**: version, links
- [ ] Settings persistence: JSON file in `%AppData%/X-Notch/settings.json`
- [ ] Startup registration: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

### Phase 9 — Gestures & Polish
**Goal**: Gesture support + animation polish.

- [ ] Mouse scroll gesture on closed notch to adjust volume
- [ ] Smooth spring animations for all state transitions
- [ ] "Hello" animation on first launch
- [ ] Sneak peek for song changes (brief inline notification)
- [ ] Fullscreen detection: auto-hide notch when a fullscreen app is active
- [ ] Animated face/idle state when no music is playing
- [ ] Keyboard shortcuts (global hotkeys to open/close, play/pause)

### Phase 10 — Packaging & Distribution
**Goal**: Installable release.

- [ ] MSIX package or Inno Setup installer
- [ ] Auto-updater (GitHub Releases check)
- [ ] GitHub Actions CI/CD pipeline
- [ ] Icon & branding

---

## 5. Key Design Decisions

### Why WPF over WinUI 3 / Electron / Tauri?
- **WPF** has the most mature support for transparent, borderless, topmost overlay windows
- `AllowsTransparency=True` + custom shapes is battle-tested
- Direct Win32 interop for `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`
- Hardware-accelerated rendering via DirectX
- Rich animation/storyboard system
- WinUI 3 has limited transparent window support; Electron is too heavy; Tauri lacks native Windows animation APIs

### Window Behavior
- The notch window is **always on top** (`Topmost=True`)
- It is **not shown in taskbar or Alt+Tab** (Win32 `WS_EX_TOOLWINDOW`)
- It does **not steal focus** (`WS_EX_NOACTIVATE`)
- It is **click-through when closed** for the transparent padding area (only the black shape is interactive)
- Positioned at `(screenWidth/2 - notchWidth/2, 0)` of the target monitor

### Animation System
Boring.notch uses SwiftUI's `interactiveSpring(response: 0.38, dampingFraction: 0.8)`. We replicate this with a custom spring solver:
```
x(t) = e^(-ζωt) * (A*cos(ωd*t) + B*sin(ωd*t))
```
Where ζ = damping ratio, ω = natural frequency, ωd = damped frequency. Applied to width, height, corner radii, opacity, scale.

### Notch Shape Geometry
The boring.notch shape is a rectangle with:
- Top corners: small quad-curve (radius ~6px) curving inward (the "ears")
- Bottom corners: larger quad-curve (radius ~14px) curving outward (rounded bottom)
- Animated between closed radii and open radii

---

## 6. NuGet Dependencies (Planned)

| Package | Purpose |
|---|---|
| `NAudio` | WASAPI loopback audio capture + volume control |
| `Hardcodet.NotifyIcon.Wpf` | System tray icon |
| `System.Reactive` | Reactive event streams for media/battery/volume changes |
| `Microsoft.Toolkit.Uwp.Notifications` | Toast notifications (optional) |
| `CommunityToolkit.Mvvm` | MVVM helpers (ObservableObject, RelayCommand) |
| `LottieSharp` | Lottie animation playback (idle face animation) |
| `OpenCvSharp4` | Webcam capture (alternative to WinRT MediaCapture) |
| `SkiaSharp` | High-performance 2D rendering for visualizer (optional) |

---

## 7. Matching Boring.Notch Dimensions

| Property | Closed | Open |
|---|---|---|
| Width | ~200px (matches macOS notch width) | ~600-700px |
| Height | ~32px | ~300px |
| Top corner radius | 6px | scaled |
| Bottom corner radius | 14px | scaled |
| Animation | spring(0.45s, damping 1.0) | spring(0.42s, damping 0.8) |
| Shadow | none | black 0.7 opacity, blur 6px |

These values will be adjustable in settings and DPI-scaled.

---

## 8. Priority Order

1. **Phase 1** — Window + shape (the foundation — nothing works without this)
2. **Phase 2** — Music (the killer feature, 80% of the UX)
3. **Phase 3** — System HUD (high-visibility, daily-use feature)
4. **Phase 4** — Battery (simple, high value)
5. **Phase 8** — Settings & tray (needed for usability)
6. **Phase 5** — Calendar (medium complexity)
7. **Phase 6** — File shelf (medium complexity)
8. **Phase 9** — Gestures & polish
9. **Phase 7** — Webcam (nice-to-have)
10. **Phase 10** — Packaging
