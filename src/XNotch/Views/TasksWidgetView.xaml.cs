using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;

namespace XNotch.Views;

public sealed class QuickTaskModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public partial class TasksWidgetView : UserControl
{
    private static readonly string TasksDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X-Notch");
    private static readonly string TasksFile = Path.Combine(TasksDir, "quick_tasks.json");

    private readonly List<QuickTaskModel> _tasks = [];
    private readonly List<TaskItem> _autoTasks = [];
    private NotchServices? _services;
    private bool _isAutoTasksView;

    public event Action<int>? TaskCountChanged;

    public TasksWidgetView()
    {
        InitializeComponent();
        NewTaskInput.TextChanged += (_, _) =>
        {
            InputPlaceholder.Visibility = string.IsNullOrWhiteSpace(NewTaskInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        };

        NewTaskInput.GotFocus += (_, _) =>
        {
            InputPlaceholder.Opacity = 0.35;
            InputContainerBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x30, 0xD1, 0x58));
        };

        NewTaskInput.LostFocus += (_, _) =>
        {
            InputPlaceholder.Opacity = 0.8;
            InputContainerBorder.BorderBrush = (Brush)FindResource("BorderBrush");
        };

        LoadTasks();
    }

    public void Bind(NotchServices services)
    {
        _services = services;
        _services.TaskDiscovery.TasksDiscovered += tasks => Dispatcher.Invoke(() =>
        {
            _autoTasks.Clear();
            _autoTasks.AddRange(tasks);
            UpdateAutoTaskBadge();
            if (_isAutoTasksView)
            {
                RenderAutoTasks();
            }
            NotifyCountChanged();
        });

        if (_services.TaskDiscovery.DiscoveredTasks.Count > 0)
        {
            _autoTasks.Clear();
            _autoTasks.AddRange(_services.TaskDiscovery.DiscoveredTasks);
            UpdateAutoTaskBadge();
        }
        NotifyCountChanged();
    }

    private void UpdateAutoTaskBadge()
    {
        int pending = _autoTasks.Count(t => t.Status != TaskStatusState.Completed);
        if (pending > 0)
        {
            AutoTaskCountText.Text = pending.ToString();
            AutoTaskCountBadge.Visibility = Visibility.Visible;
        }
        else
        {
            AutoTaskCountBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void NotifyCountChanged()
    {
        int totalPending = _tasks.Count(t => !t.IsCompleted) + _autoTasks.Count(t => t.Status != TaskStatusState.Completed);
        TaskCountChanged?.Invoke(totalPending);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TasksScrollViewer != null)
        {
            double targetOffset = TasksScrollViewer.VerticalOffset - (e.Delta / 2.5);
            TasksScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, TasksScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }

    private void EnsureWindowActivatedAndFocused()
    {
        var win = Window.GetWindow(this);
        if (win != null)
        {
            WindowHelper.EnableActivation(win);
            win.Activate();
        }
        NewTaskInput.Focus();
        Keyboard.Focus(NewTaskInput);
    }

    private void OnInputContainerMouseDown(object sender, MouseButtonEventArgs e)
    {
        EnsureWindowActivatedAndFocused();
        e.Handled = true;
    }

    private void OnNewTaskInputMouseDown(object sender, MouseButtonEventArgs e)
    {
        EnsureWindowActivatedAndFocused();
    }

    private void LoadTasks()
    {
        try
        {
            if (File.Exists(TasksFile))
            {
                var json = File.ReadAllText(TasksFile);
                var items = JsonSerializer.Deserialize<List<QuickTaskModel>>(json);
                if (items != null)
                {
                    _tasks.Clear();
                    _tasks.AddRange(items);
                }
            }
        }
        catch { }

        if (_tasks.Count == 0)
        {
            _tasks.Add(new QuickTaskModel { Title = "Review daily schedule", IsCompleted = true });
            _tasks.Add(new QuickTaskModel { Title = "Stay hydrated (Drink 2L water)", IsCompleted = false });
            _tasks.Add(new QuickTaskModel { Title = "Deep work focus session", IsCompleted = false });
            SaveTasks();
        }

        RenderTasks();
    }

    private void SaveTasks()
    {
        try
        {
            Directory.CreateDirectory(TasksDir);
            var json = JsonSerializer.Serialize(_tasks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TasksFile, json);
        }
        catch { }
    }

    private void OnNewTaskKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            AddNewTask();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            NewTaskInput.Text = "";
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnAddTaskClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewTaskInput.Text))
        {
            EnsureWindowActivatedAndFocused();
            return;
        }
        AddNewTask();
    }

    private void AddNewTask()
    {
        string text = NewTaskInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var task = new QuickTaskModel
        {
            Title = text,
            IsCompleted = false
        };
        _tasks.Insert(0, task);
        NewTaskInput.Text = "";
        SaveTasks();
        RenderTasks();

        EnsureWindowActivatedAndFocused();
    }

    private void OnClearCompletedClick(object sender, RoutedEventArgs e)
    {
        _tasks.RemoveAll(t => t.IsCompleted);
        SaveTasks();
        RenderTasks();
    }

    private void OnAutoTasksToggleClick(object sender, RoutedEventArgs e)
    {
        _isAutoTasksView = !_isAutoTasksView;
        if (_isAutoTasksView)
        {
            HeaderIconText.Text = "⚡";
            HeaderIconText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
            HeaderTitleText.Text = "AUTO-DISCOVERED TASKS";
            AutoTaskIcon.Text = "✓";
            AutoTaskLabel.Text = "My Tasks";
            AutoTaskIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            AutoTaskLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            InputContainerBorder.Visibility = Visibility.Collapsed;
            ClearCompletedButton.Visibility = Visibility.Collapsed;
            RenderAutoTasks();
        }
        else
        {
            HeaderIconText.Text = "✓";
            HeaderIconText.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            HeaderTitleText.Text = "TASKS & FOCUS";
            AutoTaskIcon.Text = "⚡";
            AutoTaskLabel.Text = "Auto-Tasks";
            AutoTaskIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
            AutoTaskLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
            InputContainerBorder.Visibility = Visibility.Visible;
            ClearCompletedButton.Visibility = Visibility.Visible;
            RenderTasks();
        }
    }

    private void RenderTasks()
    {
        TasksListPanel.Children.Clear();

        int pendingCount = _tasks.Count(t => !t.IsCompleted);
        PendingCountText.Text = $"{pendingCount} pending";

        if (_tasks.Count == 0)
        {
            TasksListPanel.Children.Add(NoTasksPlaceholder);
            NotifyCountChanged();
            return;
        }

        foreach (var task in _tasks)
        {
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 1.5, 0, 1.5)
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Circular Checkmark button
            var checkBorder = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1.5),
                BorderBrush = task.IsCompleted
                    ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
                    : (Brush)FindResource("BorderBrush"),
                Background = task.IsCompleted
                    ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
                    : Brushes.Transparent,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (task.IsCompleted)
            {
                checkBorder.Child = new TextBlock
                {
                    Text = "✓",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                };
            }
            checkBorder.MouseLeftButtonDown += (_, _) =>
            {
                task.IsCompleted = !task.IsCompleted;
                SaveTasks();
                RenderTasks();
            };
            Grid.SetColumn(checkBorder, 0);

            // Title
            var titleBlock = new TextBlock
            {
                Text = task.Title,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = task.IsCompleted
                    ? (Brush)FindResource("TextFaintBrush")
                    : (Brush)FindResource("TextPrimaryBrush")
            };
            if (task.IsCompleted)
            {
                titleBlock.TextDecorations = TextDecorations.Strikethrough;
            }
            Grid.SetColumn(titleBlock, 1);

            // Delete Button (x)
            var delBtn = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 18,
                Height = 18,
                Cursor = Cursors.Hand,
                ToolTip = "Delete task",
                Content = new TextBlock
                {
                    Text = "×",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                }
            };
            delBtn.Click += (_, _) =>
            {
                _tasks.Remove(task);
                SaveTasks();
                RenderTasks();
            };
            Grid.SetColumn(delBtn, 2);

            row.Children.Add(checkBorder);
            row.Children.Add(titleBlock);
            row.Children.Add(delBtn);

            rowBorder.Child = row;
            TasksListPanel.Children.Add(rowBorder);
        }

        NotifyCountChanged();
    }

    private void RenderAutoTasks()
    {
        TasksListPanel.Children.Clear();

        int pendingCount = _autoTasks.Count(t => t.Status != TaskStatusState.Completed);
        PendingCountText.Text = $"{pendingCount} auto";

        if (_autoTasks.Count == 0)
        {
            var placeholder = new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 12, 8, 12),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var pStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            pStack.Children.Add(new TextBlock
            {
                Text = "⚡ No auto-discovered tasks",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextFaintBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            pStack.Children.Add(new TextBlock
            {
                Text = "X-Notch automatically discovers TODOs & markdown checklists in projects",
                FontSize = 9,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextFaintestBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            placeholder.Child = pStack;
            TasksListPanel.Children.Add(placeholder);
            NotifyCountChanged();
            return;
        }

        foreach (var task in _autoTasks)
        {
            bool isDone = task.Status == TaskStatusState.Completed;
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 1.5, 0, 1.5)
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Circular Checkmark button
            var checkBorder = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1.5),
                BorderBrush = isDone
                    ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
                    : (Brush)FindResource("BorderBrush"),
                Background = isDone
                    ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
                    : Brushes.Transparent,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (isDone)
            {
                checkBorder.Child = new TextBlock
                {
                    Text = "✓",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                };
            }
            checkBorder.MouseLeftButtonDown += (_, _) =>
            {
                var newStatus = isDone ? TaskStatusState.Confirmed : TaskStatusState.Completed;
                var updated = task with { Status = newStatus };
                int idx = _autoTasks.IndexOf(task);
                if (idx >= 0) _autoTasks[idx] = updated;
                RenderAutoTasks();
                UpdateAutoTaskBadge();
                NotifyCountChanged();
            };
            Grid.SetColumn(checkBorder, 0);

            // Title & Project tag
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var tagRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 1) };
            var projectBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0x9F, 0x0A)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 0, 3, 0),
                Margin = new Thickness(0, 0, 4, 0)
            };
            projectBadge.Child = new TextBlock
            {
                Text = task.ProjectName,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A))
            };
            tagRow.Children.Add(projectBadge);

            if (!string.IsNullOrEmpty(task.SourceEvidence))
            {
                tagRow.Children.Add(new TextBlock
                {
                    Text = task.SourceEvidence,
                    FontSize = 8,
                    Foreground = (Brush)FindResource("TextFaintBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 140
                });
            }
            textStack.Children.Add(tagRow);

            var titleBlock = new TextBlock
            {
                Text = task.Title,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = isDone
                    ? (Brush)FindResource("TextFaintBrush")
                    : (Brush)FindResource("TextPrimaryBrush"),
                TextDecorations = isDone ? TextDecorations.Strikethrough : null,
                Opacity = isDone ? 0.45 : 1.0
            };
            textStack.Children.Add(titleBlock);
            Grid.SetColumn(textStack, 1);

            row.Children.Add(checkBorder);
            row.Children.Add(textStack);
            rowBorder.Child = row;
            TasksListPanel.Children.Add(rowBorder);
        }

        NotifyCountChanged();
    }
}
