using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;

namespace XNotch.Views.Panels;

public partial class ExpandedCommandCenterView : UserControl
{
    private NotchServices? _services;
    private bool _isSystemTabActive;
    private readonly List<TextBlock> _lyricBlocks = new();

    public event Action? CloseRequested;
    public event Action? SpotlightRequested;

    public ExpandedCommandCenterView()
    {
        InitializeComponent();
    }

    public void Bind(NotchServices services)
    {
        _services = services;

        // Hook telemetry
        _services.Hardware.SnapshotUpdated += OnHardwareSnapshotUpdated;

        // Hook coding agents
        _services.AgentMonitor.ActiveSessionsChanged += _ => Dispatcher.Invoke(UpdateAgentStatus);

        // Hook project and task discovery
        _services.ProjectDiscovery.ProjectsDiscovered += projects => Dispatcher.Invoke(() => UpdateProjects(projects));
        _services.TaskDiscovery.TasksDiscovered += tasks => Dispatcher.Invoke(() => UpdateTasks(tasks));

        // Hook lyrics events
        _services.Lyrics.LyricsLoaded += _ => Dispatcher.Invoke(UpdateLyricsContent);
        _services.Lyrics.LineChanged += _ => Dispatcher.Invoke(UpdateLyricsSync);

        // Hook media position for lyrics
        _services.Media.MediaInfo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaInfo.Position))
            {
                Dispatcher.Invoke(UpdateLyricsSync);
            }
            else if (e.PropertyName is nameof(MediaInfo.Title) or nameof(MediaInfo.Artist))
            {
                Dispatcher.Invoke(UpdateLyricsContent);
            }
        };

        UpdateGreeting();
        UpdateAgentStatus();
        UpdateProjects(_services.ProjectDiscovery.DiscoveredProjects);
        UpdateTasks(_services.TaskDiscovery.DiscoveredTasks);
    }

    private void UpdateGreeting()
    {
        int hour = DateTime.Now.Hour;
        string greeting = hour switch
        {
            >= 5 and < 12 => "Good morning, Developer",
            >= 12 and < 17 => "Good afternoon, Developer",
            >= 17 and < 22 => "Good evening, Developer",
            _ => "Working late, Developer"
        };
        GreetingText.Text = greeting;
    }

    private void UpdateAgentStatus()
    {
        if (_services == null) return;
        var active = _services.AgentMonitor.ActiveSessions.FirstOrDefault();
        if (active != null)
        {
            AgentGlyph.Text = "⚡";
            AgentStatusTitle.Text = $"{active.DisplayName} ({active.Phase})";
            AgentStatusDetail.Text = $"{active.ProjectPath} • {active.GitBranch} • {active.ElapsedDuration.ToString(@"mm\:ss")}";
            FocusAgentTerminalButton.Visibility = Visibility.Visible;
        }
        else
        {
            AgentGlyph.Text = "💤";
            AgentStatusTitle.Text = "No Agent Active";
            AgentStatusDetail.Text = "Background monitors watching for Claude, Codex & Antigravity";
            FocusAgentTerminalButton.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateProjects(IReadOnlyList<ProjectRecord> projects)
    {
        ProjectsItemsControl.ItemsSource = null;
        ProjectsItemsControl.ItemsSource = projects;
        ProjectCountText.Text = projects.Count.ToString();
    }

    private void UpdateTasks(IReadOnlyList<TaskItem> tasks)
    {
        TasksItemsControl.ItemsSource = null;
        TasksItemsControl.ItemsSource = tasks;
        TaskCountText.Text = tasks.Count(t => t.Status != TaskStatusState.Completed).ToString();
    }

    private void OnHardwareSnapshotUpdated(HardwareSnapshot snap)
    {
        if (!_isSystemTabActive) return;

        Dispatcher.Invoke(() =>
        {
            // CPU
            CpuUsageText.Text = $"{snap.CpuUsagePercent:F1} %";
            CpuProgressBar.Value = Math.Clamp(snap.CpuUsagePercent, 0, 100);
            CpuInfoText.Text = $"Clock: {snap.CpuClockGhz:F2} GHz";

            // RAM
            RamUsageText.Text = $"{snap.RamUsagePercent:F1} %";
            RamProgressBar.Value = Math.Clamp(snap.RamUsagePercent, 0, 100);
            RamDetailText.Text = $"{snap.RamUsedGb:F1} GB / {snap.RamTotalGb:F1} GB";

            // GPU
            if (snap.GpuTemperatureCelsius > 0)
            {
                GpuTempText.Text = $"{snap.GpuTemperatureCelsius:F0} °C";
                GpuTempText.Foreground = snap.GpuTemperatureCelsius switch
                {
                    > 80 => new SolidColorBrush(Color.FromRgb(255, 69, 58)),
                    > 70 => new SolidColorBrush(Color.FromRgb(255, 159, 10)),
                    _ => new SolidColorBrush(Color.FromRgb(48, 209, 88))
                };
            }
            else
            {
                GpuTempText.Text = "-- °C";
            }
            GpuNameText.Text = snap.GpuName;
            VramDetailText.Text = $"VRAM: {snap.GpuDedicatedVramUsedGb:F1} GB / {snap.GpuDedicatedVramTotalGb:F1} GB";
            if (snap.GpuDedicatedVramTotalGb > 0)
                VramProgressBar.Value = Math.Clamp((snap.GpuDedicatedVramUsedGb / snap.GpuDedicatedVramTotalGb) * 100.0, 0, 100);

            // Disk & Net
            DiskDetailText.Text = $"{snap.DiskDriveLetter} Usage: {snap.DiskUsagePercent:F0} % ({snap.DiskFreeGb:F0} GB free)";
            DiskProgressBar.Value = Math.Clamp(snap.DiskUsagePercent, 0, 100);
            NetworkDetailText.Text = $"Down: {snap.NetDownloadKbps:F0} KB/s • Up: {snap.NetUploadKbps:F0} KB/s";
        });
    }

    private void UpdateLyricsContent()
    {
        if (_services == null) return;
        LyricsTrackTitle.Text = $"{_services.Media.MediaInfo.Title} — {_services.Media.MediaInfo.Artist}";
        var lines = _services.Lyrics.CurrentLines;

        LyricsContainerPanel.Children.Clear();
        _lyricBlocks.Clear();

        if (lines.Count == 0)
        {
            NoLyricsNotice.Visibility = Visibility.Visible;
            LyricsContainerPanel.Children.Add(NoLyricsNotice);
            return;
        }

        NoLyricsNotice.Visibility = Visibility.Collapsed;
        foreach (var l in lines)
        {
            var tb = new TextBlock
            {
                Text = l.Text,
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = l.Timestamp
            };
            _lyricBlocks.Add(tb);
            LyricsContainerPanel.Children.Add(tb);
        }
    }

    private void UpdateLyricsSync()
    {
        if (_services == null || _lyricBlocks.Count == 0) return;
        var pos = _services.Media.MediaInfo.Position;

        for (int i = 0; i < _lyricBlocks.Count; i++)
        {
            var block = _lyricBlocks[i];
            var ts = (TimeSpan)block.Tag;
            bool isCurrent = false;

            if (i < _lyricBlocks.Count - 1)
            {
                var nextTs = (TimeSpan)_lyricBlocks[i + 1].Tag;
                if (pos >= ts && pos < nextTs) isCurrent = true;
            }
            else
            {
                if (pos >= ts) isCurrent = true;
            }

            if (isCurrent)
            {
                block.FontSize = 15;
                block.FontWeight = FontWeights.Bold;
                block.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            }
            else
            {
                block.FontSize = 13;
                block.FontWeight = FontWeights.Normal;
                block.Foreground = (System.Windows.Media.Brush)FindResource("TextDimBrush");
            }
        }
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (ViewOverview == null) return;

        ViewOverview.Visibility = TabOverview.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewProjects.Visibility = TabProjects.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewTasks.Visibility = TabTasks.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewSystem.Visibility = TabSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewLyrics.Visibility = TabLyrics.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        // Consumer gating for hardware monitor (zero CPU when not on system tab)
        if (TabSystem.IsChecked == true)
        {
            _isSystemTabActive = true;
            _services?.Hardware.RegisterConsumer();
        }
        else if (_isSystemTabActive)
        {
            _isSystemTabActive = false;
            _services?.Hardware.UnregisterConsumer();
        }

        if (TabLyrics.IsChecked == true)
        {
            UpdateLyricsContent();
            UpdateLyricsSync();
        }
    }

    private async void OnRescanClick(object sender, RoutedEventArgs e)
    {
        if (_services == null) return;
        await _services.ProjectDiscovery.StartScanAsync();
        await _services.TaskDiscovery.DiscoverTasksAsync(_services.ProjectDiscovery.DiscoveredProjects);
        UpdateGreeting();
        UpdateAgentStatus();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }

    private void OnSpotlightQuickClick(object sender, RoutedEventArgs e)
    {
        SpotlightRequested?.Invoke();
    }

    private void OnFocusAgentTerminalClick(object sender, RoutedEventArgs e)
    {
        var active = _services?.AgentMonitor.ActiveSessions.FirstOrDefault();
        if (active != null && active.ProcessId > 0)
            TerminalFocusHelper.FocusProcessWindow(active.ProcessId);
    }

    private void OnResumeProjectClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string rootPath && _services != null)
        {
            var proj = _services.ProjectDiscovery.DiscoveredProjects.FirstOrDefault(p => p.Path == rootPath);
            if (proj != null)
                ProjectMemoryService.ResumeWork(proj);
        }
    }

    private void OnTaskChecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string taskId && _services != null)
        {
            _services.TaskDiscovery.UpdateTaskStatus(taskId, TaskStatusState.Completed);
        }
    }

    private void OnTaskUnchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string taskId && _services != null)
        {
            _services.TaskDiscovery.UpdateTaskStatus(taskId, TaskStatusState.Confirmed);
        }
    }
}
