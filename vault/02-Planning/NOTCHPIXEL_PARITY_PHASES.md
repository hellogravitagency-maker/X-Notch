# NotchPixel Parity — Phased Plan

Source of the feature list: [[V0.5.118_DEEP_DIVE]] (`vault/04-Research/NotchPixel/`).
Rule for every phase: a clean-room build, event-driven over polling, motion per the
Dynamic Island notes below, and one runnable test per non-trivial piece.

## Dynamic Island UI/UX research (Apple HIG: Live Activities, ActivityKit, WWDC23 10194)

| Apple rule | X-Notch application |
|---|---|
| **Compact** = leading + trailing views around the camera, presenting *one* cohesive piece of information | Agent compact: leading = state dot + agent name; trailing = elapsed or "needs you" |
| **Minimal** = detached circle or oval when 2+ activities run; the system picks one to show attached | Music keeps the compact slot and the agent shrinks to one coloured dot in the badge row |
| **Expanded** = touch and hold / tap shows detail | Tapping the compact or minimal view opens the island on the Agents card |
| "Your Live Activity doesn't need to display everything" | Compact shows one state and one number; detail lives in the card |
| "Don't draw attention to the Dynamic Island" | No bouncing or flashing; only a slow 900 ms breathing dot while working |
| Legible at a glance; avoid small text | 11 px semibold minimum in compact, tabular (Consolas) digits for time |
| Shape follows content, spring physics, concentric corners | Existing `SpringAnimator` (response/damping, SwiftUI model) drives open, close and peek |

Motion rules (Emil Kowalski design engineering, applied):
- Enter: scale 0.96 plus a fade at 180 ms with strong ease-out, never from scale(0). Exit is faster than enter.
- Frequent actions (dock clicks, carousel arrows) get **no** motion beyond instant press feedback.
- Decorative cursor tracking (dock magnification) runs a per-frame critically damped approach. It is interruptible and has no fixed duration.
- Rare moments get delight (dock launch bounce).
- `SystemParameters.ClientAreaAnimation == false` (Windows "Animation effects" off) disables movement and keeps opacity.

## Phase status

| Phase | Scope | Status |
|---|---|---|
| 1 | **Agents**: `AgentHubService` (localhost:47830), Claude Code hooks + Codex notify auto-install with backup, Agents card, island alerts | done, tested |
| 2 | **Dock**: replaces the Windows taskbar while on (crash-safe restore), pinned + running apps, magnification, launch bounce, clock, pin/unpin/quit | done; the live run is unverified by design (it hides the real taskbar) |
| 3 | **Motion and Live Activity**: agent compact/minimal presentation on the closed island | done |
| 4 | **Clipboard**: event-driven history (Text/Link/Colour/Path/Code), filters, pin, copy-back, 24 h / 50 items, DPAPI-encrypted `clipboard.dat`, skips password-manager copies | done, tested; snippets and images skipped |
| 5 | **Controls**: Wi-Fi, Bluetooth (WinRT Radios), Focus (FocusSessionManager), Sound mute, Mic mute, output/input device pickers (IPolicyConfig), battery; right-click opens Settings | done, tested read-only; Night light opens Settings (no public API) |
| 5 | Controls card: Wi-Fi, Bluetooth, Night light, Focus, Mute, output/input device pickers | |
| 6 | **Game mode**: per-app rules Normal/Slim/Hide/Game (event-driven foreground hook) + fullscreen default; click-through game strip (FPS from ETW DXGI presents, CPU, GPU+temp, RAM, clock) | done, tested; FPS needs admin or Performance Log Users; DirectX only |
| 7 | **Widgets card**: weather + AQI (Open-Meteo), currency (Frankfurter/ECB), ping, screen time, countdown, world clock, Pomodoro focus→break, idle pet | done, tested; stocks skipped (no free keyless source); pet lives in the card, not yet on the closed island |
| 8 | **Capture card**: Snip (Win+Shift+S), Record (Win+Shift+R, Snipping Tool), Text from screen (Windows OCR → clipboard), Colour picker (frozen-screen loupe → hex); new screenshots/recordings auto-land on the shelf via FileSystemWatcher | done, tested; no recording live activity (Snipping Tool exposes no state); in-app recorder skipped (ScreenRecorderLib forces x64-only build) |
| 9 | **Phone Deck**: Kestrel LAN server :47831 (off by default, refuses Public networks), PWA (Now playing + art/seek/volume, WebSocket trackpad with drag/scroll, type text, dock apps, lock/mute/mic/snip/desktop), pairing window opened on PC with QR + 6-digit code (5 strikes, 1 req/s, one-shot), SHA-256 token hashes, revoke | done, tested on loopback; plain HTTP on LAN (no TLS); input/actions not exercised by tests |
| 10 | **Streaming**: Twitch chat (anonymous IRC/WebSocket), YouTube chat (Data API v3, user key), click-through chat overlay, OBS via obs-websocket v5 (auth, live/rec + timecode, go live/record, scene switch), island red dot + alerts; secrets DPAPI-encrypted | done, tested (parser, docs auth vector, fake OBS server); live Twitch unverified: the dev network's Sophos TLS inspection breaks the cert chain |
| 11 | **Desktop**: live video wallpaper behind icons (WorkerW, 24H2 + legacy layouts; pauses when maximized/fullscreen; brightness; safe repaint, never re-sets the user's wallpaper) + opt-in Alt-Tab switcher (LL keyboard hook, live DWM thumbnails, no animation, Esc/arrows/Shift, click to pick) | done, tested non-invasively; switcher and wallpaper need a hands-on check; hue/saturation controls skipped |
| 12 | **Dock v2**: Store/UWP apps by AUMID (icon, name, launch, pin; also in Phone Deck), status area (tray chevron peeks the real taskbar, Wi-Fi/Ethernet + volume + battery → Win+A, clock → Win+N), dock on every screen, apps on their own screen, per-screen fullscreen hiding, taskbar restore owned by App | done, tested; tray icons are Windows' own via peek (no re-implemented tray); not run hands-on |
| 13 | **Localization**: `Loc.T()` + XAML `{l:L '…'}` (308 XAML strings converted), English-key JSON embedded per language, Windows-language auto-detect + Language setting (applies on restart), 6 languages (es, fr, de, hi, ja, zh-Hans) × 229 user-facing strings, English fallback for the rest; `source/build.py` validates keys + placeholders | done, tested; long tooltips/descriptions and older views' code-built text still English; translations are mine, not native-reviewed |

## Known ceilings (marked `ponytail:` in code)
- The dock polls once per second for running windows and taskbar re-show. Upgrade: `SetWinEventHook`.
- Dock UWP apps appear under ApplicationFrameHost. Upgrade: resolve the AUMID.
- Agent hooks carry no Windows PID, so the card focuses the first Windows Terminal window.
- The agent endpoint trusts any local process. Browsers are blocked by an `Origin` check.
