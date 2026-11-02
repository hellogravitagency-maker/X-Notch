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

public class SpotlightItem
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Category { get; set; } = "Action";
    public string IconGlyph { get; set; } = "⚡";
    public Action? Action { get; set; }
}

public partial class SpotlightLauncher : Window
{
    private NotchServices? _services;
    private readonly List<SpotlightItem> _allItems = new();

    public SpotlightLauncher()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    public void Bind(NotchServices services)
    {
        _services = services;
        RebuildIndex();
    }

    public void Open()
    {
        RebuildIndex();
        SearchTextBox.Text = string.Empty;
        FilterResults(string.Empty);
        Show();
        Activate();
        SearchTextBox.Focus();
    }

    private void RebuildIndex()
    {
        _allItems.Clear();
        if (_services == null) return;

        // 1. System Actions
        _allItems.Add(new SpotlightItem
        {
            Title = "Scan Projects & Tasks",
            Subtitle = "Trigger background scan of all approved workspace roots",
            Category = "Action",
            IconGlyph = "↻",
            Action = async () =>
            {
                await _services.ProjectDiscovery.StartScanAsync();
                await _services.TaskDiscovery.DiscoverTasksAsync(_services.ProjectDiscovery.DiscoveredProjects);
            }
        });

        _allItems.Add(new SpotlightItem
        {
            Title = "Play / Pause Media",
            Subtitle = $"{_services.Media.MediaInfo.Title} — {_services.Media.MediaInfo.Artist}",
            Category = "Media",
            IconGlyph = "⏯",
            Action = async () => await _services.Media.PlayPauseAsync()
        });

        _allItems.Add(new SpotlightItem
        {
            Title = "Next Media Track",
            Subtitle = "Skip to next track via SMTC",
            Category = "Media",
            IconGlyph = "⏭",
            Action = async () => await _services.Media.NextAsync()
        });

        _allItems.Add(new SpotlightItem
        {
            Title = "Clear File Shelf",
            Subtitle = "Remove all temporarily shelved items",
            Category = "Shelf",
            IconGlyph = "🗑",
            Action = () => _services.Shelf.Clear()
        });

        // 2. Discovered Projects
        foreach (var proj in _services.ProjectDiscovery.DiscoveredProjects)
        {
            _allItems.Add(new SpotlightItem
            {
                Title = proj.Name,
                Subtitle = $"{proj.TechStack} • {proj.GitBranch} • {proj.Path}",
                Category = "Project",
                IconGlyph = "📁",
                Action = () => ProjectMemoryService.ResumeWork(proj)
            });
        }

        // 3. Discovered Tasks
        foreach (var task in _services.TaskDiscovery.DiscoveredTasks.Take(25))
        {
            _allItems.Add(new SpotlightItem
            {
                Title = task.Title,
                Subtitle = $"{task.ProjectName} • {task.SourceEvidence} ({task.Status})",
                Category = "Task",
                IconGlyph = "✅",
                Action = () =>
                {
                    var proj = _services.ProjectDiscovery.DiscoveredProjects.FirstOrDefault(p => p.Id == task.ProjectId);
                    if (proj != null) ProjectMemoryService.ResumeWork(proj);
                }
            });
        }
    }

    private void FilterResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ResultsListBox.ItemsSource = _allItems.Take(12).ToList();
        }
        else
        {
            var q = query.Trim().ToLowerInvariant();
            var matches = _allItems
                .Where(item => item.Title.ToLowerInvariant().Contains(q) ||
                               item.Subtitle.ToLowerInvariant().Contains(q) ||
                               item.Category.ToLowerInvariant().Contains(q))
                .Take(15)
                .ToList();

            ResultsListBox.ItemsSource = matches;
        }

        if (ResultsListBox.Items.Count > 0)
            ResultsListBox.SelectedIndex = 0;
    }

    private void ExecuteCurrentSelection()
    {
        if (ResultsListBox.SelectedItem is SpotlightItem selected)
        {
            Hide();
            selected.Action?.Invoke();
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchTextBox.Text;
        ClearButton.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
        FilterResults(query);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Text = string.Empty;
        SearchTextBox.Focus();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteCurrentSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (ResultsListBox.SelectedIndex < ResultsListBox.Items.Count - 1)
            {
                ResultsListBox.SelectedIndex++;
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (ResultsListBox.SelectedIndex > 0)
            {
                ResultsListBox.SelectedIndex--;
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
            }
            e.Handled = true;
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteCurrentSelection();
    }
}
