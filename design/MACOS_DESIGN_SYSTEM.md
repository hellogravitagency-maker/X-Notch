# macOS-Inspired Liquid Glass Design System
**X-NOTCH Autonomous Desktop Intelligence & Command Center**  
*Lead Designer & Desktop Architect:* Principal Windows Desktop Engineer & UX Designer  

---

## 1. Design Philosophy & Aesthetic Vision

The visual identity of **X-NOTCH** is grounded in a macOS-inspired **Liquid Glass** aesthetic. The goal is a desktop utility that feels organic, calm, highly focused, and deeply integrated into the Windows 11 desktop canvas.

### Core Design Tenets
1. **Physical Materiality:** Surfaces behave like tinted optical glass with refraction, subtle inner specular rim highlights, and soft layered shadows.
2. **Restraint Over Clutter:** Information appears only when relevant. The resting island remains completely silent. Cards avoid neon cyberpunk glows or oversized decorative borders.
3. **Concentric Geometry:** Nested elements strictly adhere to concentric curvature rules:
   $$\text{InnerRadius} = \max(6, \text{OuterRadius} - \text{Padding})$$
   This prevents the "box-inside-a-capsule" visual disharmony common in amateur overlays.
4. **Fluid Motion as State:** State transitions (collapse, peek, full expansion, tab navigation) use continuous spring physics with momentum preservation.

---

## 2. Color System & Design Tokens

### 2.1 Dark Appearance (Default)

| Token Name | Hex / RGBA Value | Semantic Usage |
|---|---|---|
| `GlassBackground` | `#09090B` (Solid Base) | Underlying canvas anchor |
| `GlassSurface` | `rgba(13, 13, 16, 0.78)` | Translucent primary capsule surface |
| `GlassSurfaceElevated` | `rgba(28, 28, 32, 0.82)` | Nested interactive cards and wells |
| `GlassBorderSubtle` | `rgba(255, 255, 255, 0.08)` | Outer rim divider |
| `GlassBorderHighlight`| `rgba(255, 255, 255, 0.16)` | Top-edge specular inner rim light |
| `TextPrimary` | `#F5F5F7` (96% White) | Primary titles, hero numbers, active labels |
| `TextSecondary` | `#A1A1AA` (64% White) | Subtitles, artists, timestamps, units |
| `TextMuted` | `#71717A` (44% White) | Inactive labels, subtle metadata |
| `AccentPrimary` | `#8B7CF6` (Vibrant Iris) | Default interactive accent, focus rings |
| `StatusActive` | `#30D158` (Apple Green) | Playing media, agent working, charging |
| `StatusWarning` | `#FF9F0A` (Amber) | Awaiting approval, battery low, warnings |
| `StatusError` | `#FF453A` (Coral Red) | Build failure, agent crash, critical alert |
| `StatusInfo` | `#0A84FF` (System Blue) | Downloads, sync, notifications |

### 2.2 Light Appearance (Adaptive)

| Token Name | Hex / RGBA Value | Semantic Usage |
|---|---|---|
| `GlassBackground` | `#F5F5F7` | Light canvas base |
| `GlassSurface` | `rgba(255, 255, 255, 0.82)`| Frosted light glass surface |
| `GlassSurfaceElevated` | `rgba(240, 240, 243, 0.90)`| Elevated light wells and cards |
| `GlassBorderSubtle` | `rgba(0, 0, 0, 0.06)` | Clean hairline borders |
| `GlassBorderHighlight`| `rgba(255, 255, 255, 0.60)`| Light bounce highlight |
| `TextPrimary` | `#1D1D1F` (90% Black) | High-contrast dark typography |
| `TextSecondary` | `#6E6E73` (55% Black) | Secondary descriptions |
| `AccentPrimary` | `#6E5CE7` | Deep purple interactive accent |

---

## 3. Typography & Hierarchy

X-NOTCH uses **Segoe UI Variable** with optical sizing axes as the native Windows fallback, automatically upgrading to **SF Pro Display / Text** when detected on the host system.

| Typography Role | Font Face | Size | Weight | Line Height | Tracking |
|---|---|---|---|---|---|
| **Hero Metric** | Segoe UI Variable Display | 44–56 px | Bold (700) | 1.05 | -0.03em |
| **Section Title** | Segoe UI Variable Display | 18–20 px | SemiBold (600)| 1.20 | -0.015em |
| **Card Header** | Segoe UI Variable Text | 13–14 px | SemiBold (600)| 1.25 | -0.01em |
| **Body Primary** | Segoe UI Variable Text | 12–13 px | Regular (400) | 1.35 | normal |
| **Metadata / Caption**| Segoe UI Variable Text | 10–11 px | Medium (500) | 1.30 | +0.02em |
| **Monospace / Code** | Cascadia Code / Consolas | 11–12 px | Regular (400) | 1.40 | normal |

---

## 4. Eight Canonical Interface States

### 1. Idle Notch (Resting Capsule)
- **Geometry:** 200×32 px pill (or user-configured width), corner radius 16 px.
- **Visuals:** Pure near-black floating glass, subtle 1px border. 
- **Content:** Minimalist digital clock (`10:42`), tiny accent dot for background activity (e.g. green for agent running), subtle battery badge if below 20%.

### 2. Live Activity Capsule
- **Geometry:** Morphs dynamically from 200px to 295–340px with squishy spring physics.
- **Triggers:** Music playback starting, coding agent running a step, build in progress, Pomodoro timer active.
- **Visuals:** Shows the primary icon, bold status phrase, and active progress ring or indicator.

### 3. Media Capsule (Expanded & Collapsed)
- **Collapsed (320×34 px):** Left: 24px round album art with spin or subtle scale; Center: Dynamic synchronized lyric line (with 180ms crossfade) or Track Peek (title over artist); Right: 6-band loudness DSP waveform.
- **Expanded Card (420×190 px):** 64px rounded album art with dominant-color shadow glow; Title, artist, and album with auto-marquee; Draggable scrub bar with timestamp indicators; Previous, Play/Pause, Next, Lyrics toggle, and Minimize toggle; Player switcher chevrons (`‹` / `›`) when multiple SMTC sessions exist.

### 4. Developer Capsule
- **Geometry:** 340×36 px (collapsed) / 480×260 px (expanded).
- **Content:**
  - Agent badge: Claude (``), Codex (``), Antigravity (``), OpenCode.
  - Project name and current active Git branch.
  - Current phase: `Running tool (bash)`, `Thinking`, `Awaiting Approval`, `Completed`.
  - Session elapsed time.
  - Interactive Action: Click brings the exact Windows Terminal tab to the foreground.

### 5. Notification Capsule (Toast Alerts)
- **Geometry:** 360×42 px.
- **Content:** 24px application icon, Sender / App Title, concise notification message (truncated with ellipsis), timestamp.
- **Behavior:** Slides into view over 0.3s, holds for 3.5s, collapses smoothly back to resting pill. Right-click or drag dismisses.

### 6. Expanded Command Center
- **Geometry:** 620×440 px floating liquid glass panel, corner radius 24 px.
- **Structure:**
  - **Left Segmented Sidebar (140 px):** Clean vertical navigation: Overview, Projects, Tasks, Agents, System, Media, Automations, Settings.
  - **Main Content Area (480 px):** Modular 2-column or 3-column card grid containing the active tab's cards.
  - **Footer Bar:** Quick status indicators, mute toggle, and Spotlight search shortcut (`Ctrl+Space`).

### 7. Spotlight Command Launcher
- **Geometry:** Centered 580×64 px input bar expanding to 580×340 px with search results.
- **Keyboard-First:** Triggered via global hotkey (`Alt+Space` or `Win+Shift+N`).
- **Capabilities:** Instant fuzzy search across projects, tasks, open windows, terminal sessions, and approved automation commands.

### 8. Focus Mode
- **Geometry:** 280×44 px minimal pill expanding to full-screen zen timer on demand.
- **Content:** Current task title, countdown timer (`24:59`), subtle amber/emerald progress line, critical alerts only.

---

## 5. Motion Design & Animation Specification

Animations utilize a centralized physical configuration rather than fragmented hardcoded milliseconds.

```csharp
public static class SpringTokens
{
    // Tactile hover peek
    public static readonly SpringConfig Peek = new(Response: 0.30, Damping: 0.78);

    // Smooth modal expansion
    public static readonly SpringConfig Open = new(Response: 0.38, Damping: 0.82);

    // Snappy settle into bezel
    public static readonly SpringConfig Close = new(Response: 0.35, Damping: 0.86);

    // Elastic drag-and-drop file shelf stretch
    public static readonly SpringConfig ShelfStretch = new(Response: 0.32, Damping: 0.75);

    // Tab content crossfade duration
    public static readonly TimeSpan ContentFade = TimeSpan.FromMilliseconds(180);
}
```

- **Reduced Motion Support:** When `SystemParameters.ClientAreaAnimation` is false or the user toggles "Reduced Motion" in settings, springs fall back to a direct 120ms cubic bezier ease without overshoot.
