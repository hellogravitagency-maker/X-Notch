# X-Notch Example Plugins

This directory contains example plugins demonstrating the X-Notch plugin system capabilities.

## Included Examples

### 1. Better Animation Plugin
**Type:** Animation Plugin
**File:** `BetterAnimationPlugin/`

Demonstrates how to create custom animations for the notch:
- Replaces default spring animations with elastic ease effects
- Adds fade in/out transitions
- Includes scale "pop" effects
- Shows how to implement all animation types (expand, collapse, peek)

**Key Features:**
- Custom easing functions
- Storyboard creation
- Transform animations
- Complete animation replacement

### 2. ChatGPT Add-on Plugin
**Type:** UI Plugin + Service Plugin
**File:** `ChatGPTAddonPlugin/`

Demonstrates a complex integration plugin:
- Custom UI panel in expanded notch
- Text and voice input support
- Settings panel integration
- Indicator in closed state
- Simulated API integration

**Key Features:**
- Custom WPF controls
- Multiple UI injection points
- Event handling
- Async operations
- Settings management

## Building the Examples

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 or VS Code

### Build Instructions

1. Navigate to this directory:
   ```bash
   cd Examples/Plugins
   ```

2. Build a specific plugin:
   ```bash
   dotnet build BetterAnimationPlugin/BetterAnimationPlugin.csproj -c Release
   ```

   Or build all plugins:
   ```bash
   dotnet build -c Release
   ```

3. Find the compiled DLLs in:
   ```
   BetterAnimationPlugin/bin/Release/net8.0-windows/BetterAnimationPlugin.dll
   ChatGPTAddonPlugin/bin/Release/net8.0-windows/ChatGPTAddonPlugin.dll
   ```

## Installing the Example Plugins

1. Copy the plugin DLL to your X-Notch plugins directory:
   ```
   %AppData%\X-Notch\Plugins\<plugin-id>\
   ```

   For example:
   ```
   %AppData%\X-Notch\Plugins\com.X-Notch.betteranimation\BetterAnimationPlugin.dll
   %AppData%\X-Notch\Plugins\com.X-Notch.chatgpt\ChatGPTAddonPlugin.dll
   ```

2. Restart X-Notch

3. The plugins will be automatically discovered and loaded

4. Enable/disable plugins from the Settings panel

## Plugin Library Example

The `example-library.json` file demonstrates the format for the online plugin library. This is the format used by the `PluginLibraryService` to discover and download plugins.

### Library Format

```json
{
  "version": "1.0",
  "plugins": [
    {
      "id": "com.example.plugin",
      "name": "Plugin Name",
      "version": "1.0.0",
      "author": "Author Name",
      "description": "Plugin description",
      "downloadUrl": "https://...",
      "category": "Animation|Integration|Widget|...",
      "permissions": ["network", "filesystem"],
      "sha256": "hash for verification"
    }
  ]
}
```

## Creating Your Own Plugin

Use these examples as templates:

1. **For Animation Plugins:** Start with `BetterAnimationPlugin`
2. **For UI/Integration Plugins:** Start with `ChatGPTAddonPlugin`
3. **For Simple Service Plugins:** Combine concepts from both

See the main [Plugin Development Guide](../../PLUGIN_DEVELOPMENT.md) for detailed instructions.

## Plugin Ideas

Here are some ideas for plugins you could create:

### Animation & Visuals
- Glassmorphism theme
- Particle effects
- Neon glow animations
- Retro CRT effect

### Integrations
- Discord Rich Presence
- Twitter/X feed
- GitHub notifications
- Slack messages
- Email notifications
- Todo list (Todoist, Microsoft To Do)

### Media & Entertainment
- YouTube controls
- Twitch viewer
- Reddit reader
- News ticker

### Productivity
- Pomodoro timer
- Quick notes
- Clipboard manager
- Screenshot tool
- Screen recorder controls

### System Utilities
- Network monitor
- CPU/RAM usage
- Disk space indicator
- Process manager
- Windows shortcuts

### Gaming
- Game launcher
- Discord voice indicator
- Steam friends status
- FPS counter

## Contributing

If you create a useful plugin:

1. Add it to the [X-Notch-Plugins](https://github.com/Gravit/X-Notch-Plugins) repository
2. Submit a PR to add it to the library.json
3. Share it with the community!

## Support

For questions about plugin development:
- Read the [Plugin Development Guide](../../PLUGIN_DEVELOPMENT.md)
- Check the [API Reference](https://github.com/Gravit/X-Notch/wiki)
- Ask in [GitHub Discussions](https://github.com/Gravit/X-Notch/discussions)
- Report issues: [GitHub Issues](https://github.com/Gravit/X-Notch/issues)
