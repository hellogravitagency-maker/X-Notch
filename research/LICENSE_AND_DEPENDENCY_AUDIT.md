# License & Dependency Audit
**X-NOTCH Autonomous Desktop Intelligence**  
*Auditor:* Principal Windows Desktop Engineer & Software Architect  

---

## 1. Reference Repositories License Analysis

| Project | License | Source / File Reference | Usage Permissibility & Clean-Room Strategy |
|---|---|---|---|
| **X-Notch** | **MIT** | `LICENSE` (Neural Creativity) | Base repository. Fully permissive. Allows modification, distribution, commercial use, and sublicensing. |
| **dynamic-island-v2** | **MIT** | `LICENSE` (F0rger123) | Fully permissive. Named pipe protocol patterns and CLI discovery algorithms can be directly adapted. |
| **Fork-Dynamic-Island-Windhawk**| **GPL-3.0** | `LICENSE` (Himanshu / ramensoftware) | **Clean-room reimplementation only.** Source code cannot be directly copied into an MIT codebase. Windows native API sequences (D3DKMT, SMTC, WMI, WASAPI) and generic mathematical formulas (RBJ bandpass filter coefficients, Fletcher-Munson loudness weighting) are public domain / non-copyrightable facts. |
| **HyperLand** | **MIT** | `README.md` (Tanmay Tiwari) | Fully permissive. Aesthetic concepts and layout measurements can be referenced freely. |
| **V-Notch** | **GPL-3.0** | `LICENSE` (rainaku) | **Clean-room reimplementation only.** We observe the DXGI enumeration sequence and consumer-gated thread pattern, but implement all C# classes from scratch. |
| **WNotch** | **GPL-3.0** | `LICENSE` (Brick-Bread) | **Clean-room reimplementation only.** The process scanning heuristic (`Process.GetProcesses()`, marker matching) and agent approval state machine are independently authored in X-NOTCH. |
| **pillar-dynamic-island** | **MIT** | `LICENSE` (warpirate) | Fully permissive. Design tokens, color palette, and UI state models can be referenced. |
| **fly-island** | **Apache-2.0** | `LICENSE` (flyl1u) | Permissive with attribution. Used for layout and widget organization concepts. |
| **LyricIsland** | **GPL-3.0** | `LICENSE` (BochengYao) | **Clean-room reimplementation only.** The public REST API of LRCLIB (`https://lrclib.net/api/get`) and standard LRC file format `[mm:ss.xx]` are open specifications. `LyricsService.cs` is written independently using `System.Net.Http` and `System.Text.Json`. |
| **WinIsland** | **MIT** | `LICENSE` (zHeuzy2) | Fully permissive. UserNotificationListener WinRT usage pattern and camera preview logic. |

---

## 2. Clean-Room Implementation Protocol

To guarantee absolute compliance with intellectual property standards and ensure X-NOTCH remains unencumbered under the **MIT License**, the following engineering rules are enforced:

1. **No Direct Code Copying from GPL Repositories:** No source code, header files, or comments from GPL-3.0 repositories (`Fork-Dynamic-Island-Windhawk`, `V-Notch`, `WNotch`, `LyricIsland`) are copied into X-NOTCH.
2. **Algorithm & API Extraction:**
   - Under established copyright law (*Google LLC v. Oracle America, Inc.* and *Baker v. Selden*), software functional mechanisms, APIs, communication protocols, and mathematical formulas are not copyrightable subject matter.
   - We extract only:
     - Documented Win32/WinRT APIs (e.g. `D3DKMTQueryAdapterInfo`, `GlobalMemoryStatusEx`, `UserNotificationListener`).
     - Mathematical constants and filter transfer functions (RBJ audio filters, spring differential equations).
     - Standard JSON schema definitions.
3. **Independent C# Authoring:** Every class, method, struct, and XAML template integrated into X-NOTCH is written from scratch, adhering to modern C# 12 and .NET 8 idioms.

---

## 3. Dependency Inventory & Footprint Budget

### 3.1 Existing X-Notch Dependencies
- `Microsoft.Windows.SDK.Contracts` (Version 10.0.22621.0+) — WinRT APIs for SMTC, Battery, UserNotificationListener.
- `CommunityToolkit.Mvvm` (Version 8.2+) — High-performance observable models, commands, and messaging.
- `NAudio` (or direct WASAPI interop) — Loopback audio capture.

### 3.2 Additions Required for Full Feature Integration
- **`Microsoft.Data.Sqlite`** (Version 8.0+)
  - *Purpose:* Local, zero-latency persistence for discovered projects, confirmed tasks, project memory, and lyric cache metadata.
  - *Footprint:* ~1.5 MB DLLs. Negligible CPU impact. Zero background memory when idle.
- **`Vortice.DXGI`** (Version 3.6+)
  - *Purpose:* Lightweight, high-performance DXGI interop for GPU enumeration, dedicated VRAM metrics, and hardware adapter metadata without needing heavy DirectX runtime wrappers.
  - *Footprint:* ~300 KB DLL.
- **`System.Management`** (Optional / Native P/Invoke Preferred)
  - *Recommendation:* Avoid heavy WMI whenever direct Win32/PDH/D3DKMT APIs exist. For CPU and GPU temperature, use direct P/Invoke calls (`GetSystemTimes`, `GlobalMemoryStatusEx`, and `D3DKMTQueryAdapterInfo`) to eliminate WMI COM overhead and service delays.

---

## 4. Security & Privacy Audit

1. **Local-Only Execution:** All project scanning, task discovery, and agent tracking operate strictly on local disk and in local memory.
2. **User-Approved Directory Boundaries:** The Project Discovery Engine scans only directories explicitly designated by the user (e.g., `P:\MyProjects`). Root drive scanning (`C:\`, `D:\`) is strictly prohibited.
3. **Zero Shell Concatenation:** Process execution in the bridge utilizes `ProcessStartInfo.ArgumentList` instead of passing concatenated command strings to `cmd.exe` or `powershell.exe`, preventing arbitrary code execution.
4. **IPC Sandboxing:** Named pipes are configured with `PipeOptions.CurrentUserOnly` and restricted ACLs, preventing unauthorized cross-user or network access.
5. **No Telemetry or Credential Harvesting:** X-NOTCH never reads `.env` files, API keys, SSH keys, or git credentials. External AI requests require opt-in consent.
