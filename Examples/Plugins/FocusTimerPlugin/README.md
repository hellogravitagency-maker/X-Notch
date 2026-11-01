# Focus Timer

`Focus Timer` adds a clean Pomodoro-style timer to the expanded X-Notch view.

## What it does

- Runs focus, short-break, and long-break sessions
- Tracks how many focus sessions you completed today
- Rotates into a long break after a configurable number of focus blocks
- Supports light and dark themes
- Includes a compact vertical-friendly layout for side docking

## Setup

You can configure Focus Timer directly from `Plugin Manager` by clicking `Configure` on the installed plugin.

The plugin also creates:

`%AppData%\X-Notch\Plugins\com.X-Notch.focustimer\settings.json`

Default file:

```json
{
  "focusMinutes": 25,
  "shortBreakMinutes": 5,
  "longBreakMinutes": 15,
  "longBreakEvery": 4
}
```

Editing the file manually is still supported, but the manager UI is the recommended setup path.
