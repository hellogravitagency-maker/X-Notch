# X-Notch-Plugins Repository Structure Template

This document outlines the recommended structure for the X-Notch-Plugins repository, which will host the official plugin library and community-contributed plugins.

## Repository Structure

```
X-Notch-Plugins/
├── README.md                          # Main repository documentation
├── CONTRIBUTING.md                    # Plugin submission guidelines
├── library.json                       # Master plugin library (loaded by X-Notch)
├── .github/
│   └── workflows/
│       ├── validate-plugin.yml        # CI to validate plugin submissions
│       └── update-library.yml         # Auto-update library.json on releases
├── plugins/
│   ├── BetterAnimation/
│   │   ├── README.md
│   │   ├── icon.png
│   │   ├── manifest.json
│   │   ├── screenshots/
│   │   │   └── demo.gif
│   │   └── releases/
│   │       └── v1.0.0/
│   │           └── BetterAnimationPlugin.dll
│   ├── ChatGPTAddon/
│   │   ├── README.md
│   │   ├── icon.png
│   │   ├── manifest.json
│   │   ├── screenshots/
│   │   └── releases/
│   └── PluginTemplate/                # Template for new plugins
│       ├── README.md
│       ├── manifest.json
│       └── src/
└── tools/
    ├── generate-hash.ps1              # Generate SHA256 for DLLs
    └── validate-manifest.ps1          # Validate manifest.json
```

## library.json Format

```json
{
  "version": "1.0",
  "lastUpdated": "2026-04-19T00:00:00Z",
  "plugins": [
    {
      "id": "com.X-Notch.betteranimation",
      "name": "Better Animation",
      "version": "1.0.0",
      "author": "X-Notch Team",
      "description": "Enhanced animations with elastic easing and fade effects",
      "minimumX-NotchVersion": "0.2.3",
      "downloadUrl": "https://github.com/N3uralCreativity/X-Notch-Plugins/releases/download/betteranimation-v1.0.0/BetterAnimationPlugin.dll",
      "homepage": "https://github.com/N3uralCreativity/X-Notch-Plugins/tree/main/plugins/BetterAnimation",
      "iconUrl": "https://raw.githubusercontent.com/N3uralCreativity/X-Notch-Plugins/main/plugins/BetterAnimation/icon.png",
      "category": "Animation",
      "permissions": [],
      "dependencies": [],
      "sha256": "HASH_HERE",
      "releaseDate": "2026-04-19T00:00:00Z",
      "isVerified": true
    }
  ]
}
```

## README.md Template

````markdown
# X-Notch-Plugins

Official plugin repository for [X-Notch](https://github.com/N3uralCreativity/X-Notch) - extending the Dynamic Island experience on Windows.

## Featured Plugins

### 🎨 Animation
- **Better Animation** - Enhanced animations with elastic easing and smooth transitions

### 🔌 Integration
- **ChatGPT Add-on** - AI assistant integration with voice and text support

### 📊 Widgets
- Coming soon...

### 🎵 Media
- Coming soon...

## Installing Plugins

### Via X-Notch (Recommended)
1. Open X-Notch Settings
2. Navigate to Plugin Manager
3. Click "Browse Plugins"
4. Search and install

### Manual Installation
1. Download the plugin DLL from [Releases](https://github.com/N3uralCreativity/X-Notch-Plugins/releases)
2. Create folder: `%AppData%\X-Notch\Plugins\<plugin-id>\`
3. Place the DLL in the folder
4. Restart X-Notch

## Creating Plugins

See the [Plugin Development Guide](https://github.com/N3uralCreativity/X-Notch/blob/main/PLUGIN_DEVELOPMENT.md) for comprehensive documentation.

Quick start:
```bash
# Clone the template
git clone https://github.com/N3uralCreativity/X-Notch-Plugins
cd X-Notch-Plugins/plugins/PluginTemplate

# Follow the template README
```

## Contributing

We welcome plugin contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Submission Process

1. Develop your plugin following the [Development Guide](https://github.com/N3uralCreativity/X-Notch/blob/main/PLUGIN_DEVELOPMENT.md)
2. Test thoroughly with X-Notch
3. Create a folder in `plugins/<YourPluginName>/`
4. Add your plugin files (README, manifest, icon, DLL)
5. Submit a Pull Request

### Requirements

- ✅ Complete `manifest.json`
- ✅ README with description and usage
- ✅ Icon (PNG, 256x256)
- ✅ SHA256 hash included
- ✅ Tested with latest X-Notch version
- ✅ No malicious code
- ✅ Open source preferred (but not required)

## Plugin Library

The plugin library is automatically updated when plugins are released. The master `library.json` is fetched by X-Notch to populate the plugin browser.

## License

Individual plugins may have their own licenses. Check each plugin's README for details.

The repository infrastructure is licensed under GPL-3.0.
````

## CONTRIBUTING.md Template

````markdown
# Contributing to X-Notch-Plugins

Thank you for your interest in contributing a plugin to X-Notch!

## Before You Start

1. Read the [Plugin Development Guide](https://github.com/N3uralCreativity/X-Notch/blob/main/PLUGIN_DEVELOPMENT.md)
2. Check existing plugins to avoid duplicates
3. Ensure your plugin adds meaningful value

## Plugin Submission Guidelines

### 1. Plugin Requirements

Your plugin must:
- ✅ Be fully functional with the latest X-Notch release
- ✅ Include a complete `manifest.json`
- ✅ Have a descriptive README
- ✅ Include an icon (256x256 PNG)
- ✅ Have no critical bugs or crashes
- ✅ Not contain malicious code or trackers
- ✅ Respect user privacy

### 2. Folder Structure

```
plugins/<YourPluginName>/
├── README.md              # Plugin documentation
├── manifest.json          # Plugin metadata
├── icon.png              # 256x256 PNG icon
├── screenshots/          # Optional screenshots
│   └── demo.gif
└── releases/
    └── v1.0.0/
        └── YourPlugin.dll
```

### 3. manifest.json Format

```json
{
  "id": "com.yourname.pluginname",
  "name": "Your Plugin Name",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "Brief description (max 200 chars)",
  "minimumX-NotchVersion": "0.2.3",
  "downloadUrl": "https://github.com/N3uralCreativity/X-Notch-Plugins/releases/download/yourplugin-v1.0.0/YourPlugin.dll",
  "homepage": "https://github.com/N3uralCreativity/X-Notch-Plugins/tree/main/plugins/YourPluginName",
  "iconUrl": "https://raw.githubusercontent.com/N3uralCreativity/X-Notch-Plugins/main/plugins/YourPluginName/icon.png",
  "category": "Animation|Integration|Productivity|Media|SystemUtility|Theme|Widget|Other",
  "permissions": ["network", "filesystem"],
  "dependencies": [],
  "sha256": "COMPUTED_HASH_HERE",
  "releaseDate": "2026-04-19T00:00:00Z",
  "isVerified": false
}
```

### 4. README Template

Your plugin's README should include:

````markdown
# Your Plugin Name

Brief one-line description.

## Features

- Feature 1
- Feature 2
- Feature 3

## Installation

### Via X-Notch Plugin Manager
1. Open X-Notch Settings → Plugin Manager
2. Search for "Your Plugin Name"
3. Click Install

### Manual Installation
1. Download from [Releases](link)
2. Place in `%AppData%\X-Notch\Plugins\<plugin-id>\`
3. Restart X-Notch

## Usage

Explain how to use your plugin.

## Configuration

If your plugin has settings, explain them here.

## Permissions

Explain why your plugin needs each permission.

## Screenshots

![Screenshot](screenshots/demo.gif)

## Development

If open source, explain how to build from source.

## License

Specify your license.

## Author

Your Name - [GitHub](https://github.com/yourusername)
````

### 5. Generating SHA256 Hash

```powershell
# Windows PowerShell
$hash = Get-FileHash YourPlugin.dll -Algorithm SHA256
$hash.Hash.ToLower()
```

### 6. Submission Process

1. **Fork** this repository
2. **Create** a branch: `git checkout -b add-plugin-yourname`
3. **Add** your plugin folder to `plugins/`
4. **Create** a GitHub Release with your DLL
5. **Update** `library.json` (optional - we can do this)
6. **Submit** a Pull Request

### 7. Pull Request Template

```markdown
## Plugin Submission: [Plugin Name]

### Plugin Details
- **Name**: Your Plugin Name
- **Version**: 1.0.0
- **Category**: Animation/Integration/etc.
- **Author**: Your Name

### Checklist
- [ ] manifest.json is complete and valid
- [ ] README.md is included
- [ ] Icon (256x256 PNG) is included
- [ ] SHA256 hash is computed and included
- [ ] Tested with X-Notch 0.2.3+
- [ ] No malicious code
- [ ] Permissions are justified in README

### Description
Brief description of what your plugin does and why it's useful.

### Screenshots
(Optional) Include screenshots or GIFs

### Testing
Describe how you tested the plugin.
```

## Code of Conduct

- Be respectful to other contributors
- Accept constructive feedback gracefully
- Prioritize user experience and safety
- Give credit where credit is due

## Review Process

1. **Automated Checks**: CI validates manifest format and checks for obvious issues
2. **Code Review**: Maintainers review the code for quality and safety
3. **Testing**: Plugin is tested with current X-Notch version
4. **Approval**: Once approved, plugin is merged and added to library.json
5. **Verification**: Official X-Notch Team plugins get `isVerified: true`

## Questions?

- Check the [Plugin Development Guide](https://github.com/N3uralCreativity/X-Notch/blob/main/PLUGIN_DEVELOPMENT.md)
- Ask in [GitHub Discussions](https://github.com/N3uralCreativity/X-Notch/discussions)
- Open an [Issue](https://github.com/N3uralCreativity/X-Notch-Plugins/issues)

Thank you for contributing! 🎉
````

## Files to Create in X-Notch-Plugins Repository

1. `README.md` - Main repo documentation
2. `CONTRIBUTING.md` - Contribution guidelines
3. `library.json` - Master plugin library
4. `plugins/PluginTemplate/` - Template for new plugins
5. `.github/workflows/validate-plugin.yml` - CI for validation
6. `tools/generate-hash.ps1` - Hash generation script
7. `tools/validate-manifest.ps1` - Manifest validation script

This structure provides a professional, maintainable foundation for the plugin ecosystem.
