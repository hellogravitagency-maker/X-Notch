---
tags: [map, repo]
updated: 2026-11-22
---

# Folder Map

Back to [[00 Home]].

```
X-Notch/
├── src/
│   ├── XNotch/              main WPF app (net8.0-windows), see [[Codebase Map]]
│   └── XNotch.PluginSdk/    NuGet SDK for plugin authors
├── tests/XNotch.Tests/      xUnit tests (services, themes, XAML load, DB)
├── Examples/Plugins/        12 sample plugins + example-library.json (CI builds these)
├── templates/plugin/        starter template for a new plugin
├── installer/X-Notch.iss    Inno Setup script (used by release.yml)
├── docs/                    GitHub Pages site (index.html, documentation/, assets/)
├── prototypes/              HTML/CSS liquid-glass UI experiments (was SidePorjects/)
├── vault/                   this Obsidian vault: all planning, design, research notes
├── .github/workflows/       ci, release, manage-plugins, publish-plugin-sdk
├── X-Notch.sln, build.bat, README.md, LICENSE
```

## Vault layout
| Folder | Holds |
|---|---|
| `01-Architecture/` | target architecture, integration protocol, design system, perf budget |
| `02-Planning/` | master plan, implementation roadmap |
| `03-Plugins/` | plugin dev guide, system summary, AI prompt, plugins-repo template |
| `04-Research/` | feature comparison, reference audits, licence audit |
| `04-Research/NotchPixel/` | NotchPixel teardown docs; `raw/` = inspector dumps (`user_*` files gitignored) |

## Removed on 2026-11-22 cleanup
| Path | Why | Size |
|---|---|---|
| `X-Notch/` (nested, untracked) | abandoned 4-project rewrite (~2.4k LOC), 179 MB exe, debug .py scripts. Docs kept in `04-Research/NotchPixel/` | 622 MB |
| `research/reference_repos/` | 9 cloned repos, gitignored. URLs in [[Reference Repos]] | 498 MB |
| `publish/`, all `bin/`, `obj/` | build output, regenerate with `dotnet build` / `build.bat` | ~700 MB |
| `architecture/ design/ performance/ roadmap/ research/` | merged into `vault/` | – |
