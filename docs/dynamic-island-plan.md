# Dynamic Island redesign: plan

Branch: `feat/dynamic-island`. Baseline commit `71f94b0` snapshots the WIP that was uncommitted before the redesign, so every phase commit builds from a clean checkout. Baseline: `dotnet build -c Release` is green, and all 153 tests pass.

## How it works today (Phase 0 findings)

| Area | Today | What the redesign uses |
| --- | --- | --- |
| Window | `NotchWindow` is a **fixed-size** layered window (`AllowsTransparency=True`), sized once by `UpdateFixedWindowBounds()` to the largest open size plus `WindowPadding` 20. The window sits 2 DIP above the screen top. The shape animates *inside* `NotchCanvas`. | Keep. Dynamic mode adds a bigger bottom margin (`IslandMetrics.WindowGlowMargin`), so the 40 px glow and the 10/40 shadow are never clipped. |
| Hit-testing | Layered windows hit-test per pixel: pixels with alpha 0 pass clicks to the windows below. Invisible layers turn off `IsHitTestVisible` when opacity drops below 0.5. | Keep as is. It already holds on every frame, so no `WS_EX_TRANSPARENT` toggling is needed. The new content layer follows the same opacity rule. |
| Shape | `NotchShape.CreateNotchGeometry(rect, topR, bottomR)`. `topR > 0.5` gives the attached notch with ears. `topR = 0` gives a floating pill with four squircle corners. | Floating style passes `topR = 0` with a capsule radius. Attached style keeps the ears. Goo combines pill, circle and bridge into one geometry. |
| Springs | `SpringAnimator`: an analytic damped spring with response/damping. `AnimateTo` already inherits `CurrentVelocity` when it is called mid-flight. However, `CurrentValue` is only refreshed on `Tick()`, so a retarget between frames starts from a value up to one frame old. | Add `Retarget(target[, t])`: it samples value and velocity at the *current* time, then re-aims. This makes retargets exactly continuous. Add a static `AnimationStarted` event so the window can subscribe to `CompositionTarget.Rendering` only while something is moving. |
| Frame loop | `OnRendering` is subscribed for the whole window lifetime, so WPF renders every frame even when idle. | Subscribe on demand and unsubscribe once every spring settles. |
| States | `NotchState { Closed, Peeking, Open }` on `NotchViewModel`. | Unchanged. It is the interaction layer. `IslandPresentationMachine` sits on top of it. |
| Alerts | Hand-rolled capsules (notification, power surge, lock keys, USB, operating mode), each with 3 springs and a `DispatcherTimer`. | Classic keeps them. Dynamic routes USB, timer done, low battery, audio device, deck and reminder/briefing alerts through `AlertQueue` and shows them in the Alert presentation. Notifications, lock keys and operating mode stay on their classic capsules (out of scope). |
| HUD | `HudOverlay` (volume/brightness) raises `HudShown`/`HudDismissed` and widens the pill. It has its own 2 s timer. | Dynamic: the same hooks feed `IslandPresentationMachine.PingHud()` (1.5 s). The visuals are a new `HudActivityView`. Classic HUD is untouched. |
| Spotlight | A separate `SpotlightLauncher` window (search and launch logic in its code-behind). | **Deviation:** the island morphs to 600×56 on `ExpandActivity`. Then the existing launcher window is placed exactly over the morphed shape, with its own pop animation off and a black island fill. On screen it reads as the island becoming the field, and the search/launch logic stays untouched. Moving the logic into the island would be a refactor the spec forbids. |
| Media | `MediaService` (SMTC) + `MediaInfo.DominantColor` (art color is already extracted). `AudioCaptureService.SpectrumData` has 12 bands. | Music activity: 4 bars downsampled from `SpectrumData`. The glow tint already comes from art via `ToGlowTint`. `GetDominantColor` widens to `BitmapSource`. |
| Timer | `CountdownTimerService` (`Tick`, `StateChanged`, `Completed`). | Timer activity. |
| Stopwatch | Exists only **inside** `StopwatchWidgetView` (a private `Stopwatch`). There is no service. | Add a read-only `IsRunning`/`Elapsed` and a `RunningChanged` event to the view (additive). |
| Agents | `AgentHubService.Sessions` with `AgentState.Working`. | Agent activity while any session is `Working`. |
| Shelf | `ShelfService.HasItems` / `Items` / `ShelfChanged`. | Shelf activity. |
| USB | `UsbDeviceService.UsbDriveConnected/Disconnected`. | Alert. |
| Battery | `BatteryService.ChargePercent` (observable). There is **no** low-battery event. | Crossing 20 % / 10 % while discharging raises the alert. This is a threshold on the existing value, not new detection. |
| Audio device | `AudioCaptureService.OnDefaultDeviceChanged` (an NAudio `IMMNotificationClient`) reinitialises capture. | Add a `DefaultRenderDeviceChanged(string name)` event there (additive). |
| Phone Deck | `DeckPairing.Paired`, `DeckServer.StateChanged`. There is no per-phone connect/disconnect event, and `DeckServer.cs` is off limits. | "Deck connected" maps to `Paired`, and "Deck off" maps to the server state going `Off`. A per-phone disconnect alert is a known gap. |
| Reminders | `SmartReminderEngine.ReminderTriggered`, `DailyBriefingService.BriefingReady`. | Alerts. |
| Game mode | `FullscreenService.FullscreenChanged`. | `IslandPresentationMachine.SetGameMode`. |
| Themes | `NotchFillBrush` per dictionary (Light = `#F5F5F7`). `ThemeService` rewrites it per palette. | New `IslandFillBrush`: `#000000` in Dark and Light. Liquid Glass keeps its glass material. Classic still uses `NotchFillBrush`, so it does not change. |
| Settings | `AppSettings` is persisted as **JSON** (`settings.json`). Sqlite (`XNotchDatabase`) holds other data, not settings. | **Conflict with the spec ("Sqlite settings layer"):** the new `IslandMode` and `IslandStyle` go through the existing JSON settings layer, which is the real one. |
| Plugin SDK | The interfaces live in `src/XNotch/Plugins` (namespace `XNotch.Plugins`). `src/XNotch.PluginSdk` only packs the app's reference assembly and README. | **Conflict:** `ILiveActivityPlugin` / `IIslandAlerts` go in `src/XNotch/Plugins`, which ships inside the SDK. The SDK csproj gets the minor version bump (0.8.1 to 0.9.0) and its README documents the new interfaces. |
| Animation plugins | `IAnimationPlugin` is Storyboard-based, and `GetAnimationPlugins()` is never called. No plugin overrides spring presets today. | Spec condition "if an IAnimationPlugin currently overrides presets" is false. `IslandSprings` exposes a `Resolve(name)` hook with an override map, so a future mechanism has one place to plug into. Nothing is wired to plugins (YAGNI). |
| Localization | English text is the key. Each locale is a JSON map. A test requires more than 200 entries, no empty values, and matching `{n}` holes. | Add every new string to all 6 locale files (English text as fallback). |
| Reduced motion | `SpringAnimator.Reduced` = user setting OR `!SystemParameters.ClientAreaAnimation`. | Reused. Under reduced motion, springs become 150 ms fades, and goo, squash and glow rotation are off. |

## New model

```
NotchState (Closed/Peeking/Open)    interaction layer, unchanged
        │
IslandPresentationMachine           pure C#; inputs: activities, interaction, AlertQueue, game mode, IClock
        │  → Presentation {Idle, Compact, Minimal, Expanded, Alert, Spotlight, Hud} + Primary + Secondary
        ▼
NotchWindow (Dynamic && Top dock)   maps presentation → IslandMetrics target → existing width/height/radius springs
                                    + IslandContentLayer (compact/minimal/alert/hud views, choreographed)
```

Dynamic presentations apply to the **top dock only**. On Left/Right dock the island keeps the current Closed/Peeking/Open visuals, and Alerts/Hud use the current HUD positioning. Classic mode skips every new path, so it renders exactly as in v0.9.0.

## Files

Added:
- `src/XNotch/Island/IslandPresentation.cs`, `IslandPresentationMachine.cs`, `ActivityPriorityResolver.cs`, `AlertQueue.cs`, `IClock.cs`
- `src/XNotch/Island/IslandMetrics.cs`, `IslandSprings.cs`, `GooBridgeGeometry.cs`, `RollingDigits.cs` (pure diff helper)
- `src/XNotch/Island/Activities/ILiveActivity.cs` + Music, Timer, Stopwatch, Agent, Shelf activities
- `src/XNotch/Island/IslandContentLayer.cs` (WPF host for compact/minimal/alert/hud content)
- `src/XNotch/Controls/RollingNumberText.cs`
- `src/XNotch/Plugins/ILiveActivityPlugin.cs`, `IIslandAlerts.cs`
- `tests/XNotch.Tests/Island*Tests.cs`

Changed: `SpringAnimation.cs` (Retarget plus a start event), `NotchWindow.xaml(.cs)` (Dynamic branches and content layer), `AppSettings.cs` (2 settings), `SettingsWindow.cs` (2 rows), the theme dictionaries and `ThemeService` (`IslandFillBrush`), `ColorExtractionService` (`BitmapSource`), `AudioCaptureService` (device-changed event), `StopwatchWidgetView.xaml.cs` (expose state), the locale JSON files, `FocusTimerPlugin`, and the SDK csproj/README.

Untouched (per spec): `DeckServer.cs`, installer, `build.bat`, `.github`, `PluginLibraryService`, auto-update, DPAPI, ETW, app version.

## Phases

1. Core model: machine, resolver, alert queue, metrics, springs, `Retarget`, goo math, digit diff, and their tests.
2. Window: Dynamic mode/style settings, presentation, shape morph with choreography and squash, on-demand frame loop, glow margin.
3. Activities: content layer, rolling numbers, music/timer/stopwatch/agent/shelf, goo minimal plus swap, glow ring.
4. Alerts, HUD and Spotlight morph.
5. Plugin SDK additions and the Focus Timer live activity.
6. Settings UI, locales, final notes.

---

## Outcome (phases 1–6)

Commits on `feat/dynamic-island`, one per phase:

| Phase | Commit summary |
| --- | --- |
| 0 | `docs:` this plan |
| 1 | presentation machine, priority resolver, alert queue, metrics, springs, `Retarget` |
| 2 | dynamic presentation in the window, shape morph and choreography |
| 3 | live activities, rolling numbers, gooey minimal, assistant glow |
| 4 | alerts, HUD and Spotlight morph |
| 5 | `ILiveActivityPlugin` / `IIslandAlerts`, Focus Timer live activity, SDK 0.9.0 |
| 6 | settings rows, locale strings, these notes |

After every phase, `dotnet build -c Release` and `dotnet test tests/XNotch.Tests` passed (189 tests). The single-file `win-x64` publish still produces `X-Notch.exe`.

### Files added

- `src/XNotch/Island/`: `IslandPresentation.cs` (enum, `IslandAlert`, `IClock`), `IslandPresentationMachine.cs`, `ActivityPriorityResolver.cs`, `AlertQueue.cs`, `IslandMetrics.cs` (+ `IslandColors`), `IslandSprings.cs`, `GooBridgeGeometry.cs`, `RollingDigits.cs`, `IslandContentLayer.cs`, `AssistantGlow.cs`
- `src/XNotch/Island/Activities/`: `ILiveActivity.cs`, `BuiltInActivities.cs` (Music, Timer, Stopwatch, Agent, Shelf), `IslandVisuals.cs` (text, glyphs, art, `ProgressRing`, `WaveformBars`, rows)
- `src/XNotch/Controls/RollingNumberText.cs`
- `src/XNotch/Plugins/ILiveActivityPlugin.cs` (`ILiveActivityPlugin`, `IIslandAlerts`)
- `src/XNotch/Views/NotchWindow.Island.cs` (presentation, choreography, squash, goo geometry, frame loop), `NotchWindow.Activities.cs` (activities, minimal circle, glow, pages, plugin activities), `NotchWindow.Alerts.cs` (alert sources, HUD, Spotlight morph)
- `tests/XNotch.Tests/IslandModelTests.cs`

### Files changed

- `Helpers/SpringAnimation.cs`: `Retarget`, static `AnimationStarted`
- `Views/NotchWindow.xaml.cs`: Dynamic branches in transitions, layout and window bounds; capsule alerts route to the queue; on-demand frame loop
- `Models/AppSettings.cs`: `IslandMode`, `IslandStyle`
- `Views/SettingsWindow.cs`: two rows under Appearance → Island shape
- Theme dictionaries and `ThemeService`: `IslandFillBrush`
- `Services/AudioCaptureService.cs`: `DefaultRenderDeviceChanged`
- `Views/StopwatchWidgetView.xaml.cs`: `IsRunning` / `Elapsed` / `RunningChanged`
- `Views/MusicPlayerView.xaml(.cs)`: names plus `ApplyIslandStyle`
- `Views/SpotlightLauncher.xaml(.cs)`: names plus the island presentation (logic untouched)
- `Plugins/PluginManager.cs`: `GetLiveActivityPlugins`
- The 6 locale files
- `Examples/Plugins/FocusTimerPlugin` (1.1.0)
- `src/XNotch.PluginSdk` (0.9.0, README)

### Tests added

- Machine: Idle → Compact → Minimal → Expanded, peek, alert overlay and restore, HUD reset, Spotlight modal and restore, game-mode suppression, swap, change events.
- Resolver: ordering, recency ties, promotion, plugin clamp, plugins can't outrank the agent.
- AlertQueue (fake clock): 2.6 s, hover pause, 1 s dedupe, FIFO max 3, 10 s game-mode hold.
- `Retarget` continuity: value and velocity within 1e-6. Expand-activity overshoot is 2.5–5.5 %.
- Goo: the neck exists below the break distance, is absent above it, and stays inside the hull.
- Rolling digits: only changed columns animate, right-aligned length changes.

### Deviations and known gaps

1. **Settings persistence:** JSON, not Sqlite. `AppSettings` lives in `settings.json`, and `XNotchDatabase` never stored settings, so the new settings use the real layer.
2. **SDK location:** the plugin interfaces are defined in `src/XNotch/Plugins`. The SDK project only packs the app's reference assembly, so the new interfaces ship there too.
3. **Animation-plugin preset overrides:** not wired. No plugin overrides spring presets today: `IAnimationPlugin` is Storyboard-based and `GetAnimationPlugins()` was never called. `IslandSprings.Overrides` / `Resolve` is the hook a future mechanism can use.
4. **Spotlight is not reparented into the island.** The island morphs to 600×56 on `ExpandActivity`. The existing launcher window is then laid exactly over it in an island style (its background is the island's own fill). The island grows to 600×360 when a query shows results. Moving the launcher's code-behind logic into the island would have meant refactoring logic the spec says to keep untouched.
5. **Goo resting gap vs. break distance.** The 8 px gap gives a centre distance of 40, which is inside the 42 px break distance. Taken literally, the neck would never snap at rest. The neck snaps on the split overshoot, or when the split settles, and stays broken until a merge starts.
6. **Reduced motion:** the shape still uses the existing reduced spring (critically damped, ≤ 0.22 s), not a 150 ms fade, because snapping the size and fading would read as a jump. Content uses 150 ms fades. Goo, squash and glow rotation are off.
7. **Hit-testing:** the existing per-pixel layered-window hit-test already passes clicks outside the shape on every frame, so there is no `WS_EX_TRANSPARENT` toggling. The camera hover strip (1/255 alpha) is hidden in Dynamic mode because it would catch clicks around the floating pill. The under-camera pop-up is therefore off in Dynamic mode; hovering the island itself still peeks.
8. **Phone Deck:** there is no per-phone connect/disconnect event, and `DeckServer.cs` is off limits. Pairing already raises a notification, which becomes an alert. The server switching on or off raises "Connected" / "Disconnected".
9. **Extra routing:** in Dynamic mode, notifications, lock keys, charging and the Armoury operating mode also show as island alerts. Their classic capsules would be clipped by the 120 px idle pill.
10. **Frame loop:** `CompositionTarget.Rendering` is now subscribed on demand in Classic mode too. This is a performance-only change; the visuals are unchanged.
11. **Verified on screen** (1920×1080 at 125 %): idle pill, the expand overshoot with content arriving after the shape, the collapse with content leaving first, the agent compact with its glow ring, alerts, the HUD, and the Spotlight morph. **Not verified live:**
    - music compact: no media was playing, and the user's player was not started
    - Minimal goo: rendered offscreen only
    - timer, stopwatch and shelf compacts
    - the 10-clicks-in-2-s stress test
    - frame time under 8 ms, idle CPU, and 150 % DPI
