# Performance Budget & Hardware Optimization Plan
**X-NOTCH Autonomous Desktop Intelligence**  
*Performance Engineer & Desktop Architect:* Principal Windows Desktop Engineer  

---

## 1. Host Target Hardware Specification

All performance budgets are calibrated for the user's primary workstation:
- **Processor:** Intel Core i7-12700H (14 cores: 6 P-cores, 8 E-cores, 20 threads)
- **Dedicated Graphics:** NVIDIA GeForce RTX 3050 Laptop GPU (4GB GDDR6 VRAM)
- **Integrated Graphics:** Intel Iris Xe Graphics
- **System Memory:** 16GB DDR5 RAM
- **Storage:** 1TB NVMe SSD
- **Operating System:** Windows 11 64-bit (22H2 / 23H2+)

The application must remain ultra-lightweight, never triggering thermal throttling, fan spin-ups, or battery drain.

---

## 2. Hard Performance Budgets

| Operational State | Max CPU Target | Max RAM Target | Max GPU Target | Rendering Cadence | Polling Strategy |
|---|---|---|---|---|---|
| **Idle Resting Notch** | **< 0.4%** | **< 60 MB** | **0.0%** | On-demand (1 Hz clock tick or event) | No continuous polling |
| **Media Playback (Waveform)**| **< 1.8%** | **< 75 MB** | **< 1.2%** | 60 FPS (Waveform on `CompositionTarget`) | 10ms WASAPI buffer (only while active) |
| **Collapsed Lyrics Streaming**| **< 2.0%** | **< 80 MB** | **< 1.5%** | 60 FPS (Text crossfade & scroll) | Event-driven playhead interpolation |
| **Expanded Command Center** | **< 3.5%** | **< 95 MB** | **< 2.5%** | 60 FPS during spring transitions | 1 Hz Hardware Sampler (Consumer-gated) |
| **Fullscreen Game / App Mode**| **< 0.1%** | **< 60 MB** | **0.0%** | Suspended (0 FPS, window hidden/tucked) | Win32 foreground hook only |
| **Project Directory Scan** | **< 8% (P-core)** | **< 110 MB (Peak)**| **0.0%** | Background I/O thread (BelowNormal priority)| Throttled incremental scan (1 dir/50ms) |

---

## 3. Core Optimization Principles

### 3.1 Consumer-Gated Hardware Sampling
- **The Problem:** Many system monitors continuously poll PDH performance counters, WMI, and GPU metrics every 500ms, consuming 2–5% CPU permanently even when minimized.
- **The Solution:** X-NOTCH implements an `AutoResetEvent` consumer gate. When the Expanded Command Center is closed or the user is looking at the Media/Projects tab, the hardware sampling thread halts entirely. CPU overhead drops to 0.00%.

### 3.2 Bounded LRU Image & Artwork Caching
- **The Problem:** Downloading high-resolution 600×600 album artwork for thousands of songs causes unbounded heap growth and memory bloat.
- **The Solution:** An in-memory `LruCache<string, BitmapSource>` strictly limits album art storage to **30 items** (~15 MB maximum memory). Older items are evicted and decoded on demand from the disk cache.

### 3.3 Zero-Allocation DSP Audio Processing
- **The Problem:** Allocating new byte/float arrays every 10ms in the WASAPI loopback callback generates massive garbage collection pressure, causing micro-stutters during animations.
- **The Solution:** The 6-band RBJ filterbank uses pinned, pre-allocated circular buffers (`float[48]`) and stack-allocated spans (`Span<float>`). Zero garbage collection heap allocations occur during active audio visualization.

### 3.4 Direct Win32 Memory & CPU Delta Calculation
- **The Problem:** Invoking WMI (`SELECT * FROM Win32_PerfFormattedData_PerfOS_Processor`) takes 50–150ms per query and loads several WMI COM DLLs.
- **The Solution:** Direct Win32 P/Invoke to `GetSystemTimes` and `GlobalMemoryStatusEx`. Execution time is under **2 microseconds** per sample.

### 3.5 D3DKMT GPU Temperature Query
- **The Problem:** Running NVML or vendor-specific GPU SDKs introduces heavy third-party DLL dependencies and elevated privileges.
- **The Solution:** Dynamic P/Invoke into Windows `gdi32.dll` `D3DKMTQueryAdapterInfo` (Type 62). This is the exact native API used by Windows Task Manager, executing in under **1 microsecond** with zero external dependencies.

---

## 4. Performance Verification & Regression Checklist

1. **Idle Verification:** Run `perfmon` or Task Manager for 10 minutes with X-NOTCH resting at the top of the screen; verify average CPU remains < 0.5% and private working set < 60 MB.
2. **Media Stress Test:** Stream continuous Spotify music for 30 minutes; verify memory stabilizes without continuous upward drift.
3. **Fullscreen Game Test:** Launch a DirectX fullscreen game (or simulate via fullscreen video); verify X-NOTCH tucks to 4px and drops all active timers and render loops.
4. **Dispatcher Responsiveness Test:** Log any UI Dispatcher frame delay exceeding 16ms (`FrameDropsThreshold`).
