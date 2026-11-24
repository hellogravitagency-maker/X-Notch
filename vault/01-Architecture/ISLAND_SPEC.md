# X-Notch Island — Design, State, Motion & Delivery Spec

Status: 2026-11-24. Companion to `MACOS_DESIGN_SYSTEM.md` (tokens) and the motion prototype
`prototypes/DynamicIsland/index.html` (look + feel reference, `SPEC` block = port contract).

**Stack decision:** stay on WPF / .NET 8. The brief prefers Electron + React, but X-Notch already ships a
working WPF island with native integrations (SMTC media, WinRT notification listener, Core Audio,
radios, hardware sensors). Rewriting would discard working code and add a Chromium runtime (~150 MB RAM)
to an always-on overlay. Everything in the brief maps onto the existing architecture below.

---

## 1. Design system

| Token | Value | Where |
|---|---|---|
| Island fill (idle/compact) | `#000000` — blends into the bezel | `DarkTheme.xaml` `NotchFillBrush` |
| Expanded glass | Liquid Glass theme (backdrop blur + rim) | `LiquidGlassTheme.xaml`, `ApplyLiquidGlassEffect` |
| Text primary / secondary / muted | `#FFFFFF` / derived from palette (`ThemePalette.TextSecondary`, `TextMuted`) | `ThemePalette.cs` |
| Accent | Windows accent or user pick (Settings → Accent colour) | `ThemeService` |
| Status | success `#30D158`, warning `#FF9F0A`, error `#FF453A` (Apple system colours) | `ThemePalette` |
| Font | Segoe UI Variable (system font; tabular figures for times) | XAML |
| Size — idle | 200 × 32 | `NotchConstants.Closed*` |
| Size — peek / audio peek | 220 × 34 / 295 × 36 | `NotchConstants.Peek*`, `AudioPeek*` |
| Size — open | 500–885 × 168 (content-measured) | `GetOpenWidth`, `GetTopOpenHeight` |
| Radii — top ears / bottom | idle 6 / 15, peek 8 / 17, open 10 / 30 | `NotchConstants.*Radius` × `CornerRadiusScale` |

Colour is contextual only: green = charging/success, orange = timers, red = recording/alerts, accent for
the active control. No neon, no glow on idle.

## 2. State transitions

```
            hover (HoverOpenDelayMs)          click / hotkey / long-hover
  Closed ───────────────────────────► Peek ─────────────────────────────► Open
    ▲  ◄──────── leave (80 ms) ──────────┘                                  │
    └────────── leave (HoverCloseDelayMs) / Esc / click-away / hotkey ──────┘

  Transient overlays (on top of Closed/Peek, never over Open):
    Notification > PowerSurge > LockState > USB > OperatingMode   (priority order)
  Activity in Closed: LiveActivity (music) · timer badge · agent compact · privacy dots · OBS live
  Per-app rule: Normal | Slim | Hidden | Game strip (FullscreenRule / AppRules)
```

Rules: every transition is a spring retarget from the *current* value (interruptible, velocity carried);
the open island collapses on Esc, click-away (`Deactivated`), hover-leave, or the global hotkey.

## 3. Component architecture (brief name → code)

| Brief component | Implementation |
|---|---|
| NotchShell, LiquidGlassSurface | `NotchWindow` + `NotchShape` geometry + glass paths |
| NotchIdle / Hover / Expanded | `ClosedContent`, `TransitionToPeek`, `OpenContent` + card carousel |
| ActivityManager / MultiActivity | `UpdateClosedContentVisibility`, capsule priority in `UpdateNotchVisuals` |
| NotchNotification, NotificationItem | `NotificationCapsule`, `NotificationService` (WinRT `UserNotificationListener`) |
| MediaPlayer | `MediaService` (SMTC), `MusicPlayerView`, `MusicLiveActivity`, `AudioVisualizer` |
| SystemMonitor, PerformanceGraph | `HardwareMonitorService`, `SystemVitalsWidgetView` |
| TimerWidget | `CountdownTimerService`, `StopwatchWidgetView` |
| QuickControls | `SystemControlsService` (radios, mic, Focus, audio devices), `ControlsWidgetView`, `VolumeService`, `BrightnessService` |
| GamingPanel | `GameModeServices`, `GameStripWindow` |
| AIAssistant | `AgentHubService`, `AgentsWidgetView` (agent activity), `SpotlightLauncher` |
| SettingsPanel | `SettingsWindow` |
| AnimationProvider | `SpringAnimator` (+ static motion preferences) |
| DisplayManager | `ScreenHelper`, per-screen islands, `HandleDisplayChange`, Per-Monitor V2 DPI |

## 4. Motion spec

Springs use Apple's response (s) / damping-ratio model (`SpringAnimator`).

| Interaction | Response | Damping | Notes |
|---|---|---|---|
| Peek (hover) | 0.28 | 0.72 | slight overshoot, feels tactile |
| Open — shape | 0.38 / 0.36 (w / h) | 0.82 / 0.80 | |
| Open — content opacity | 0.42 | 1.0 | lags the shape: shape leads, content follows |
| Open — content scale | 0.44 | 0.86 | 0.92 → 1, from the top edge |
| Close — shape | 0.34 / 0.30 | 0.86 / 0.88 | |
| Close — content | 0.16 opacity, 0.24 scale | 1.0 | exit faster than enter |
| Capsules (notification etc.) | 0.32–0.34 | 0.72–0.75 | opacity 0.24 / 0.88 |

Accessibility (applied globally in `SpringAnimator.AnimateTo`):
- **Animation speed** 0.5–2× divides every response.
- **Springiness** 0–1.5× scales overshoot; 0 = critically damped everywhere.
- **Reduce motion** (setting, or Windows *Animation effects* off) → damping 1.0, response ≤ 0.22 s.

## 5. Feature plan

| Module | Status | Next step |
|---|---|---|
| Media | Done (SMTC, art, progress, visualizer, lyrics, album-art glow) | — |
| Notifications | Done (real Windows listener, capsule, history card) | Grouping + swipe-to-dismiss |
| System monitor | Done (CPU/GPU/RAM/net/temps where sensors exist) | — |
| Timers | Done (countdown, stopwatch, focus, split-island bubble) | — |
| Quick controls | Done (Wi-Fi, BT, mic, Focus, volume, brightness, output device) | — |
| Gaming | Done (game strip, per-app rules) | — |
| Downloads | **Missing** — no reliable OS-wide download API | Shelf-folder watcher (Downloads dir, `.crdownload/.part` → done) |
| AI assistant | Partial (agent activity, Spotlight) | Prompt input card (needs a provider + key; opt-in) |
| Motion settings | **Done this pass** | — |
| Corner roundness setting | **Fixed this pass** (was saved but never applied) | — |
| Split island + album glow | **Done** — `UpdateIslandExtras()`; glow opacity 0.30 closed / 0.38 peek / 0.22 open, tint via `ColorExtractionService.ToGlowTint`; bubble spring 0.46 / 0.74 | — |

## 6. Technical risks

| Risk | Mitigation |
|---|---|
| Fullscreen/maximized apps trigger the game strip or hide the island | Per-app rules already user-controllable; keep default `FullscreenRule` conservative |
| Focus stealing | Window is non-activating except while open (`WindowHelper.EnableActivation/DisableActivation`) |
| CPU/GPU cost of per-frame geometry | `CompositionTarget.Rendering` only updates while a spring animates; idle trims working set |
| Liquid Glass backdrop capture cost | Only active in glass theme; reduced-transparency → solid |
| Multi-monitor DPI | Placement in physical px, content in DIPs scaled per screen (Per-Monitor V2) |
| Notification listener permission denied | Island keeps working without banners; access is granted in Windows Settings → Privacy → Notifications |
| Large radius clipping content | Bottom radius clamped to `min(h − topR, (w − 2·topR)/2)` in `UpdateNotchVisuals` |
