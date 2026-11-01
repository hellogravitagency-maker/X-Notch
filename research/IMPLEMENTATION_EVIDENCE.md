# Implementation Evidence & Technical Workflows
**X-NOTCH Autonomous Desktop Intelligence**  
*Auditor:* Principal Windows Desktop Engineer & Reverse Engineering Analyst  

---

## 1. Overview & Verification Method

This document details verified implementation evidence extracted through static and dynamic analysis of audited repositories. Each section provides exact source file references, Windows APIs, data structures, and the end-to-end reconstructed workflow for candidate features to be integrated into X-NOTCH.

---

## 2. Reconstructed Feature Workflows

### 2.1 SMTC Multi-Session & Active Player Switching
- **Reference Project:** `Fork-Dynamic-Island-Windhawk` (`mod.wh.cpp`, lines 4208–4350, 7710–7745)
- **Primary Windows APIs:**
  - `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`
  - `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`
  - `GetSessions()` / `GetCurrentSession()`
  - `GlobalSystemMediaTransportControlsSession.TrySkipNextAsync()`, `TrySkipPreviousAsync()`, `TryTogglePlayPauseAsync()`
  - `TryChangePlaybackPositionAsync(Int64 positionTicks)`
- **Workflow Reconstruction:**
  1. **Session Acquisition:** Background worker queries `GlobalSystemMediaTransportControlsSessionManager.GetSessions()`.
  2. **Deduplication:** Multiple tabs in Chrome/Edge share the same AppUserModelId (`SourceAppUserModelId`). Sessions are filtered and unique players indexed.
  3. **Active Session Selection:** If a pinned player (`PinnedMediaSource`) exists, that session is prioritized; otherwise, the session currently reporting `PlaybackStatus.Playing` is selected.
  4. **Player Switching Action:** Chevron buttons (`‹` / `›`) in the expanded media view cycle through the indexed session list and store the selected AUMID. A refresh event triggers immediate metadata re-polling.
  5. **Timeline Seeking:** Dragging the progress scrubber calls `TryChangePlaybackPositionAsync` with desired ticks, updating the playhead immediately without waiting for the next SMTC poll.

---

### 2.2 Perceived Loudness 6-Band DSP Waveform
- **Reference Project:** `Fork-Dynamic-Island-Windhawk` (`mod.wh.cpp`, lines 5853–5940, `WaveformAnalyzer`)
- **Primary Windows APIs:**
  - `IMMDeviceEnumerator`, `IMMDevice`, `IAudioClient`, `IAudioCaptureClient` (WASAPI Loopback)
  - `AUDCLNT_STREAMFLAGS_LOOPBACK`
- **DSP Filterbank Algorithm:**
  - WASAPI capture thread pulls PCM audio frames from the default render endpoint.
  - Downmixes multi-channel audio to mono: `M = 0.5 * (L + R)`.
  - Feeds mono samples into 6 second-order RBJ bandpass filters tuned at:
    `80 Hz, 250 Hz, 700 Hz, 2000 Hz, 5000 Hz, 11000 Hz`.
  - Accumulates energy over ~10 ms windows.
  - Applies Fletcher-Munson ear sensitivity weighting coefficients:
    `[0.55, 0.85, 1.0, 1.0, 0.9, 0.7]`.
  - Calculates spectral density: `density = (activeBands - 1) / (totalBands - 1)`.
  - Dynamic compression: Compares instantaneous dB against decaying peak reference (`decay = 3 dB/sec`), applying auto-gain up to +8 dB for quiet mastering.
  - Writes to a 48-slot circular ring buffer representing ~480 ms of history.
- **Rendering in WPF:**
  - WPF `AudioVisualizer` samples the ring buffer on `CompositionTarget.Rendering`, rendering symmetric vertical bars radiating from the center bar.

---

### 2.3 Synchronized Lyrics Engine (LRCLIB + LRU Cache)
- **Reference Project:** `LyricIsland` (`LyricHover.Core/LrcLibClient.cs`, `LrcParser.cs`, `LyricsCache.cs`) & `Fork-Dynamic-Island-Windhawk` (`mod.wh.cpp`, lines 5573–5690)
- **Primary APIs:**
  - HTTP GET: `https://lrclib.net/api/get?artist_name={artist}&track_name={title}&album_name={album}&duration={sec}`
  - Fallback search: `https://lrclib.net/api/search?q={title}+{artist}`
- **Workflow Reconstruction:**
  1. **Track Normalization:** Cleans track title by stripping regex patterns:
     `\s*[-–]\s*(Remastered|Live|Deluxe|Radio Edit|Mono).*`, `\s*\(feat\..*?\)`, `\s*\[.*?\]`.
  2. **Cache Verification:** Generates cache key: `SHA256(normalizedTitle + "\x1f" + normalizedArtist)`. Checks memory LRU dictionary; if missing, checks `%LOCALAPPDATA%\XNotch\LyricsCache\{hash}.lrc`.
  3. **Async Fetch:** If not cached, sends asynchronous HTTP request with 5-second timeout and User-Agent header. Handles HTTP 404 (marks as Not Found) and HTTP 503/429 (exponential backoff retry).
  4. **LRC Parsing:** Parses timestamp tags `[mm:ss.xx]` into `List<LyricLine>` sorted by timestamp.
  5. **Dynamic Pill Expansion:** In collapsed mode, when the playhead approaches within 350ms of an active line, the notch capsule smoothly expands from 150px to 320px. Instrumental breaks longer than 4 seconds trigger smooth contraction back to 150px with an eighth-note icon.

---

### 2.4 Hardware Monitor: CPU, RAM, GPU VRAM & Temperature
- **Reference Projects:**
  - `V-Notch` (`Services/GpuMonitorService.cs`, `Modules/SystemMonitorModule.cs`)
  - `Fork-Dynamic-Island-Windhawk` (`mod.wh.cpp`, lines 6124–6180, D3DKMT)
- **Primary Windows APIs:**
  - **Memory:** `GlobalMemoryStatusEx` (`MEMORYSTATUSEX.ullTotalPhys`, `ullAvailPhys`).
  - **CPU Usage:** `GetSystemTimes` (`IdleTime`, `KernelTime`, `UserTime`) with delta calculation:
    `CPU% = 100 * (1.0 - (idleDelta / (kernelDelta + userDelta)))`.
  - **CPU Frequency:** PDH counter `\Processor Information(_Total)\Processor Frequency` × `% Processor Performance` / 100.
  - **GPU Memory & Adapter Info:** Vortice.DXGI `IDXGIFactory1.EnumAdapters1()` -> `AdapterDescription1.DedicatedVideoMemory` and `Description`.
  - **GPU Temperature:** Dynamic P/Invoke to `gdi32.dll` `D3DKMTQueryAdapterInfo` with type 62 (`KMTQAITYPE_ADAPTERPERFDATA`). Reads `Temperature` field in deci-Celsius (`temp_C = val / 10`).
- **Resource Optimization:**
  - Hardware sampling worker thread uses an `AutoResetEvent` gate tied to UI tab visibility. When the System tab is closed or the notch is collapsed, background polling is paused completely.

---

### 2.5 Windows Toast Notification Interception
- **Reference Project:** `WinIsland` (`WinIsland/Services/NotificationService.cs`)
- **Primary Windows APIs:**
  - `Windows.UI.Notifications.Management.UserNotificationListener`
  - `Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus`
  - `KnownNotificationBindings.ToastGeneric`
- **Workflow Reconstruction:**
  1. **Access Request:** On application startup, calls `UserNotificationListener.Current.RequestAccessAsync()`.
  2. **Graceful Fallback:** If status is `Denied` (e.g. running unpackaged or blocked in Windows Settings), logs diagnostic message and disables toast features without crashing.
  3. **Event Polling / Notification Arrival:** Periodically inspects `GetNotificationsAsync(NotificationKinds.Toast)`.
  4. **Text & Icon Extraction:** Extracts app display name, title, and body from `binding.GetTextElements()`. Resolves app icon bitmap.
  5. **Priority Filtering:** Suppresses notifications if Windows Focus Assist / Do Not Disturb is active.
  6. **Notch Presentation:** Displays a transient notification capsule for 3.5 seconds, then smoothly returns to idle state.

---

### 2.6 Coding Agent Monitoring & In-Notch Approvals
- **Reference Project:** `WNotch` (`Notch.App/Agents/AgentTracker.cs`, `ApprovalService.cs`, `AgentCatalog.cs`)
- **Primary Mechanisms:**
  - Process scanning via `Process.GetProcesses()` matching catalog definitions:
    - Claude Code: `claude.exe` or `node.exe` with `@anthropic-ai/claude-code`
    - OpenAI Codex: `codex.exe` or `node.exe` with `@openai/codex`
    - Google Antigravity / Gemini: `agy.exe`, `gemini.exe`, or `@google/gemini-cli`
  - Duplex Named Pipe IPC: `\\.\pipe\XNotch-AgentHooks` created with `PipeOptions.CurrentUserOnly`.
  - Terminal Tab Focusing: Uses Windows UI Automation to locate the host Windows Terminal (`wt.exe`), enumerate tabs, identify the tab matching the agent's PID/working directory, and trigger `SelectionItemPattern.Select()`.
  - In-Notch Approval Workflow:
    1. Agent CLI sends JSON permission request: `{"type":"approval","tool":"bash","command":"git push","timeout":60}`.
    2. Notch animates into Amber Approval Capsule with "Allow" (`Enter`) and "Deny" (`Esc`) buttons.
    3. User clicks action or presses shortcut -> response sent over named pipe -> agent resumes immediately.

---

### 2.7 Analytical Spring Motion Engine with Momentum Continuity
- **Reference Project:** SwiftUI / Apple CoreAnimation & X-Notch working copy (`Helpers/SpringAnimation.cs`)
- **Physical Formulation:**
  $$\omega = \frac{2\pi}{\text{Response}}, \quad \zeta = \text{DampingFraction}, \quad \omega_d = \omega\sqrt{1 - \zeta^2}$$
  $$x(t) = \text{target} + e^{-\zeta\omega t} \left( A\cos(\omega_d t) + B\sin(\omega_d t) \right)$$
  $$v(t) = e^{-\zeta\omega t} \left[ (B\omega_d - A\zeta\omega)\cos(\omega_d t) - (A\omega_d + B\zeta\omega)\sin(\omega_d t) \right]$$
- **Momentum Continuity:**
  When a new animation target is requested while an ongoing animation is mid-flight, the animator inherits the instantaneous velocity $v(t)$ as the starting velocity of the new trajectory. This completely eliminates jarring direction changes and matches authentic iOS/macOS fluid physics.

---

## 3. Windows Compatibility & Reliability Matrix

| Component | Target Windows Versions | Fallback Behavior on Older / Restricted OS |
|---|---|---|
| **SMTC Media Controls** | Win 10 (19041+) & Win 11 | Fallback to empty idle capsule when media session unavailable |
| **WASAPI Loopback Capture** | Win 10 & Win 11 | Waveform renders resting dots if no audio rendering device active |
| **LRCLIB Synced Lyrics** | Win 10 & Win 11 (requires internet) | Renders beamed eighth-notes icon if no lyrics found or offline |
| **D3DKMT GPU Temperature** | Win 10 (19041+) & Win 11 | Displays N/A when adapter perf data unavailable (e.g. software driver) |
| **Vortice.DXGI VRAM** | Win 10 & Win 11 | Gracefully omits VRAM metric if DXGI adapter query fails |
| **UserNotificationListener** | Win 10 (1809+) & Win 11 | Disables toast capture if user denies privacy permission |
| **Windows Terminal Tab Focus** | Windows 10/11 with Windows Terminal | Falls back to Win32 `SetForegroundWindow` on the terminal window |
