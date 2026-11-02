using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace XNotch.Views;

public class CommandEntry
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconGlyph { get; set; } = "⚡";
    public Action? Execute { get; set; }
}

public partial class CommandCenterView : UserControl
{
    private NotchServices? _services;
    private AppSettings? _settings;
    private readonly List<CommandEntry> _allCommands = new();
    private readonly List<CommandEntry> _filteredCommands = new();

    public event Action? CloseRequested;
    public event Action<AppSettings>? SettingsChanged;
    public event Action? OpenSettingsRequested;
    public event Action? OpenPluginsRequested;

    public CommandCenterView()
    {
        InitializeComponent();
    }

    public void Bind(NotchServices services, AppSettings settings)
    {
        _services = services;
        _settings = settings;
        BuildIndex();
    }

    public void FocusInput()
    {
        SearchInput.Text = string.Empty;
        PlaceholderText.Visibility = Visibility.Visible;
        FilterCommands(string.Empty);
        SearchInput.Focus();
    }

    public void BuildIndex()
    {
        _allCommands.Clear();
        if (_services == null || _settings == null) return;

        // 1. Productivity & Intelligence
        _allCommands.Add(new CommandEntry
        {
            Title = "Scan Projects & Workspaces",
            Subtitle = "Autonomous discovery of Git repos, active branches, and code TODOs",
            Category = "Productivity",
            IconGlyph = "↻",
            Execute = async () =>
            {
                await _services.ProjectDiscovery.StartScanAsync();
                await _services.TaskDiscovery.DiscoverTasksAsync(_services.ProjectDiscovery.DiscoveredProjects);
            }
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Morning Executive Briefing",
            Subtitle = "Synthesize recent commits, tasks, and vitals with local AI",
            Category = "AI",
            IconGlyph = "🌅",
            Execute = async () =>
            {
                var report = await _services.Briefing.GenerateBriefingAsync(
                    _services.ProjectDiscovery.DiscoveredProjects,
                    _services.TaskDiscovery.DiscoveredTasks,
                    _services.Hardware.CurrentSnapshot);
                _services.Notifications.AddNotification("Morning Briefing", report.Headline, report.Summary);
            }
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Start Focus Timer (25m)",
            Subtitle = "Pomodoro deep work sprint",
            Category = "Productivity",
            IconGlyph = "⏳",
            Execute = () => _services.Timer.Start(TimeSpan.FromMinutes(25), "Focus")
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Log Water Intake (+250 ml)",
            Subtitle = $"Current: {_settings.WaterCurrentMl} ml / {_settings.WaterGoalMl} ml",
            Category = "Wellness",
            IconGlyph = "💧",
            Execute = () =>
            {
                _settings.WaterCurrentMl += 250;
                _settings.Save();
                SettingsChanged?.Invoke(_settings);
            }
        });

        // 2. Media Controls
        _allCommands.Add(new CommandEntry
        {
            Title = "Play / Pause Media",
            Subtitle = $"{_services.Media.MediaInfo.Title} — {_services.Media.MediaInfo.Artist}",
            Category = "Media",
            IconGlyph = "⏯",
            Execute = async () => await _services.Media.PlayPauseAsync()
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Next Track",
            Subtitle = "Skip to next track via Windows SMTC",
            Category = "Media",
            IconGlyph = "⏭",
            Execute = async () => await _services.Media.NextAsync()
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Previous Track",
            Subtitle = "Skip back to previous track via SMTC",
            Category = "Media",
            IconGlyph = "⏮",
            Execute = async () => await _services.Media.PreviousAsync()
        });

        // 3. System & Utilities
        _allCommands.Add(new CommandEntry
        {
            Title = "Volume Up (+5%)",
            Subtitle = $"Current volume: {(_services.Volume.Volume * 100):F0}%",
            Category = "System",
            IconGlyph = "🔊",
            Execute = () => _services.Volume.SetVolume(Math.Min(1.0f, _services.Volume.Volume + 0.05f))
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Volume Down (-5%)",
            Subtitle = $"Current volume: {(_services.Volume.Volume * 100):F0}%",
            Category = "System",
            IconGlyph = "🔉",
            Execute = () => _services.Volume.SetVolume(Math.Max(0.0f, _services.Volume.Volume - 0.05f))
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Toggle Mute",
            Subtitle = "Mute or restore master system volume",
            Category = "System",
            IconGlyph = "🔇",
            Execute = () => _services.Volume.ToggleMute()
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Toggle Dark / Light Theme",
            Subtitle = $"Current theme: {_settings.Theme}",
            Category = "Appearance",
            IconGlyph = "🎨",
            Execute = () =>
            {
                _settings.Theme = _settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
                _settings.AdaptToWindowsTheme = false;
                _settings.Save();
                SettingsChanged?.Invoke(_settings);
            }
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Toggle Liquid Glass Material",
            Subtitle = "Frosted glass refraction and acrylic blur",
            Category = "Appearance",
            IconGlyph = "🪟",
            Execute = () =>
            {
                _settings.Theme = AppTheme.LiquidGlass;
                _settings.AdaptToWindowsTheme = false;
                _settings.Save();
                SettingsChanged?.Invoke(_settings);
            }
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Clear Notification History",
            Subtitle = "Dismiss all recorded notifications",
            Category = "Notifications",
            IconGlyph = "🧹",
            Execute = () => _services.Notifications.ClearHistory()
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Clear File Shelf",
            Subtitle = "Remove all temporarily staged items",
            Category = "Shelf",
            IconGlyph = "🗑",
            Execute = () => _services.Shelf.Clear()
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Open Settings",
            Subtitle = "Configure display, dock, theme, and behavior",
            Category = "Navigation",
            IconGlyph = "⚙",
            Execute = () => OpenSettingsRequested?.Invoke()
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Open Plugin Manager",
            Subtitle = "Browse, install, and manage community plugins",
            Category = "Navigation",
            IconGlyph = "🧩",
            Execute = () => OpenPluginsRequested?.Invoke()
        });

        _allCommands.Add(new CommandEntry
        {
            Title = "Lock Workstation",
            Subtitle = "Lock current Windows user session",
            Category = "System",
            IconGlyph = "🔒",
            Execute = () =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation")
                    {
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        });

        // 4. Discovered Projects
        foreach (var proj in _services.ProjectDiscovery.DiscoveredProjects)
        {
            _allCommands.Add(new CommandEntry
            {
                Title = $"Project: {proj.Name}",
                Subtitle = $"{proj.Path} • {proj.GitBranch ?? "main"}",
                Category = "Projects",
                IconGlyph = "📂",
                Execute = () =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = proj.Path,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            });
        }

        FilterCommands(SearchInput.Text);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchInput.Text;
        PlaceholderText.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        FilterCommands(query);
    }

    private void FilterCommands(string query)
    {
        _filteredCommands.Clear();
        string q = (query ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(q))
        {
            _filteredCommands.AddRange(_allCommands);
        }
        else
        {
            _filteredCommands.AddRange(_allCommands.Where(c =>
                c.Title.ToLowerInvariant().Contains(q) ||
                c.Subtitle.ToLowerInvariant().Contains(q) ||
                c.Category.ToLowerInvariant().Contains(q)));
        }

        CommandsListBox.ItemsSource = null;
        CommandsListBox.ItemsSource = _filteredCommands;
        if (_filteredCommands.Count > 0)
        {
            CommandsListBox.SelectedIndex = 0;
        }

        CountSummaryText.Text = $"{_filteredCommands.Count} commands";
    }

    private void OnSearchInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (CommandsListBox.SelectedIndex < _filteredCommands.Count - 1)
            {
                CommandsListBox.SelectedIndex++;
                CommandsListBox.ScrollIntoView(CommandsListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (CommandsListBox.SelectedIndex > 0)
            {
                CommandsListBox.SelectedIndex--;
                CommandsListBox.ScrollIntoView(CommandsListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void OnCommandsListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    private void ExecuteSelected()
    {
        if (CommandsListBox.SelectedItem is CommandEntry item)
        {
            CloseRequested?.Invoke();
            item.Execute?.Invoke();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }
}
