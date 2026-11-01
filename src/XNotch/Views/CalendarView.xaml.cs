using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.IO.Path;

namespace XNotch.Views;

public sealed class CalendarTaskReminderModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; }
    public string Title { get; set; } = "";
    public string? Time { get; set; }
    public bool IsReminder { get; set; } // true = reminder, false = task
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public partial class CalendarView : UserControl
{
    private static readonly string CalendarDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X-Notch");
    private static readonly string CalendarDataFile = Path.Combine(CalendarDataDir, "calendar_tasks_reminders.json");

    private readonly List<CalendarTaskReminderModel> _calendarItems = [];
    private bool _isReminderMode;

    private CalendarService? _calendarService;
    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _selectedDate = DateTime.Today;
    private readonly DispatcherTimer _dayCheckTimer;

    public CalendarView()
    {
        InitializeComponent();
        LoadCalendarItems();
        RenderCalendarGrid();
        UpdateDateGlanceVisuals();

        NewTaskReminderInput.TextChanged += (_, _) =>
        {
            TaskReminderInputPlaceholder.Visibility = string.IsNullOrWhiteSpace(NewTaskReminderInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        };

        NewTaskReminderInput.GotFocus += (_, _) =>
        {
            TaskReminderInputPlaceholder.Opacity = 0.35;
            TaskReminderInputBorder.BorderBrush = new SolidColorBrush(_isReminderMode
                ? Color.FromArgb(0x80, 0xFF, 0x9F, 0x0A)
                : Color.FromArgb(0x80, 0x30, 0xD1, 0x58));
        };

        NewTaskReminderInput.LostFocus += (_, _) =>
        {
            TaskReminderInputPlaceholder.Opacity = 0.7;
            TaskReminderInputBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
        };

        _dayCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _dayCheckTimer.Tick += (_, _) =>
        {
            if (_selectedDate == null || _selectedDate.Value.Date == DateTime.Today.AddDays(-1))
            {
                RenderCalendarGrid();
            }
        };
        _dayCheckTimer.Start();
    }

    private void LoadCalendarItems()
    {
        try
        {
            if (File.Exists(CalendarDataFile))
            {
                string json = File.ReadAllText(CalendarDataFile);
                var items = JsonSerializer.Deserialize<List<CalendarTaskReminderModel>>(json);
                if (items != null)
                {
                    _calendarItems.Clear();
                    _calendarItems.AddRange(items);
                }
            }
        }
        catch { }
    }

    private void SaveCalendarItems()
    {
        try
        {
            Directory.CreateDirectory(CalendarDataDir);
            string json = JsonSerializer.Serialize(_calendarItems, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CalendarDataFile, json);
        }
        catch { }
        UpdateDateGlanceVisuals();
    }

    public void ResetToGlanceView()
    {
        DateGlanceView.Visibility = Visibility.Visible;
        MonthGridView.Visibility = Visibility.Collapsed;
        AgendaView.Visibility = Visibility.Collapsed;
        TaskReminderView.Visibility = Visibility.Collapsed;
        UpdateDateGlanceVisuals();
    }

    public void ShowFullCalendar()
    {
        DateGlanceView.Visibility = Visibility.Collapsed;
        MonthGridView.Visibility = Visibility.Visible;
        AgendaView.Visibility = Visibility.Collapsed;
        TaskReminderView.Visibility = Visibility.Collapsed;
        RenderCalendarGrid();
    }

    private void OnDateGlanceClick(object sender, MouseButtonEventArgs e)
    {
        ShowFullCalendar();
    }

    private void OnBackToGlanceClick(object sender, RoutedEventArgs e)
    {
        ResetToGlanceView();
    }

    public void UpdateDateGlanceVisuals()
    {
        if (GlanceMonthText == null || GlanceDayNumberText == null) return;

        var today = DateTime.Today;
        GlanceMonthText.Text = today.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        GlanceDayNumberText.Text = today.Day.ToString();
        GlanceDayOfWeekText.Text = today.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
        GlanceDayTitleText.Text = today.ToString("dddd", CultureInfo.InvariantCulture);
        GlanceFullDateText.Text = today.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

        int tasksCount = _calendarItems.Count(i => i.Date.Date == today && !i.IsCompleted);
        int eventCount = _calendarService?.TodayEvents.Count ?? 0;

        if (eventCount > 0 && tasksCount > 0)
        {
            GlanceSnippetText.Text = $"📅 {eventCount} events • ✓ {tasksCount} tasks";
        }
        else if (eventCount > 0)
        {
            var firstEvent = _calendarService?.TodayEvents.FirstOrDefault();
            GlanceSnippetText.Text = $"🕒 {firstEvent?.Start:t} {firstEvent?.Title}";
        }
        else if (tasksCount > 0)
        {
            GlanceSnippetText.Text = $"✓ {tasksCount} pending items today";
        }
        else
        {
            GlanceSnippetText.Text = "📅 No events scheduled today";
        }
    }

    private void OnAgendaPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (AgendaScrollViewer != null)
        {
            double targetOffset = AgendaScrollViewer.VerticalOffset - (e.Delta / 2.5);
            AgendaScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, AgendaScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }

    private void OnTaskReminderPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TaskReminderScrollViewer != null)
        {
            double targetOffset = TaskReminderScrollViewer.VerticalOffset - (e.Delta / 2.5);
            TaskReminderScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, TaskReminderScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }

    public void Bind(CalendarService calendarService)
    {
        _calendarService = calendarService;
        _calendarService.EventsUpdated += () => Dispatcher.Invoke(() =>
        {
            RenderCalendarGrid();
            UpdateDateGlanceVisuals();
            if (AgendaView.Visibility == Visibility.Visible)
            {
                ShowAgendaForDate(_selectedDate ?? DateTime.Today);
            }
        });
        RenderCalendarGrid();
        UpdateDateGlanceVisuals();
    }

    public void RenderCalendarGrid()
    {
        MonthYearText.Text = _displayMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();

        DaysGrid.Children.Clear();

        int firstDayOfWeek = (int)_displayMonth.DayOfWeek;
        var start = _displayMonth.AddDays(-firstDayOfWeek);

        for (int i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            bool isCurrentMonth = date.Month == _displayMonth.Month;
            bool isToday = date.Date == DateTime.Today;
            bool isSelected = date.Date == _selectedDate?.Date;
            bool hasOutlookEvent = _calendarService?.TodayEvents.Any(ev => ev.Start.Date == date.Date) == true;
            bool hasCustomItem = _calendarItems.Any(item => item.Date.Date == date.Date);
            bool hasEvent = hasOutlookEvent || hasCustomItem;

            var cellBorder = new Border
            {
                Width = 20,
                Height = 15,
                CornerRadius = new CornerRadius(7.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = date.ToString("D", CultureInfo.CurrentCulture) + (hasEvent ? " (Tasks/Reminders scheduled)" : "")
            };

            if (isToday)
            {
                cellBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)); // Authentic Apple Calendar Red
            }
            else if (isSelected)
            {
                cellBorder.Background = (Brush)FindResource("SurfaceRaisedBrush");
                cellBorder.BorderBrush = (Brush)FindResource("BorderBrush");
                cellBorder.BorderThickness = new Thickness(1);
            }
            else
            {
                cellBorder.Background = Brushes.Transparent;
            }

            var cellStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var dateText = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 8.5,
                FontWeight = (isToday || isSelected) ? FontWeights.Bold : (isCurrentMonth ? FontWeights.SemiBold : FontWeights.Normal),
                Foreground = isToday
                    ? Brushes.White
                    : (isCurrentMonth
                        ? (Brush)FindResource("TextPrimaryBrush")
                        : (Brush)FindResource("TextFaintBrush")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            cellStack.Children.Add(dateText);

            // Subtle event/reminder/task indicator dot
            if (hasEvent && !isToday)
            {
                var customItem = _calendarItems.FirstOrDefault(ci => ci.Date.Date == date.Date);
                Color dotColor = hasOutlookEvent
                    ? Color.FromRgb(0x0A, 0x84, 0xFF)
                    : (customItem != null && customItem.IsReminder ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Color.FromRgb(0x30, 0xD1, 0x58));

                var dot = new Ellipse
                {
                    Width = 2.5,
                    Height = 2.5,
                    Fill = new SolidColorBrush(dotColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0.5, 0, 0)
                };
                cellStack.Children.Add(dot);
            }

            cellBorder.Child = cellStack;

            var clickedDate = date;
            cellBorder.MouseLeftButtonUp += (_, _) =>
            {
                _selectedDate = clickedDate;
                if (clickedDate.Month != _displayMonth.Month)
                {
                    _displayMonth = new DateTime(clickedDate.Year, clickedDate.Month, 1);
                }
                RenderCalendarGrid();
            };

            // Double click opens task and reminder editor directly in the calendar
            cellBorder.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    _selectedDate = clickedDate;
                    ShowTaskReminderForDate(clickedDate);
                    e.Handled = true;
                }
            };

            DaysGrid.Children.Add(cellBorder);
        }
    }

    private void OnPrevMonthClick(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        RenderCalendarGrid();
    }

    private void OnNextMonthClick(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        RenderCalendarGrid();
    }

    private void OnTodayClick(object sender, MouseButtonEventArgs e)
    {
        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selectedDate = DateTime.Today;
        RenderCalendarGrid();
        e.Handled = true;
    }

    private void OnToggleAgendaClick(object sender, RoutedEventArgs e)
    {
        ShowAgendaForDate(_selectedDate ?? DateTime.Today);
    }

    private void OnBackToGridClick(object sender, RoutedEventArgs e)
    {
        AgendaView.Visibility = Visibility.Collapsed;
        TaskReminderView.Visibility = Visibility.Collapsed;
        MonthGridView.Visibility = Visibility.Visible;
    }

    private void OnOpenTaskReminderFromAgendaClick(object sender, RoutedEventArgs e)
    {
        ShowTaskReminderForDate(_selectedDate ?? DateTime.Today);
    }

    private void OnBackFromTaskReminderClick(object sender, RoutedEventArgs e)
    {
        TaskReminderView.Visibility = Visibility.Collapsed;
        AgendaView.Visibility = Visibility.Collapsed;
        MonthGridView.Visibility = Visibility.Visible;
        RenderCalendarGrid();
    }

    private void OnToggleItemTypeClick(object sender, RoutedEventArgs e)
    {
        _isReminderMode = !_isReminderMode;
        UpdateItemTypeVisuals();
    }

    private void UpdateItemTypeVisuals()
    {
        if (ItemTypeText == null || ItemTypePill == null || NewTaskReminderInput == null || AddTaskReminderButton == null)
            return;

        if (_isReminderMode)
        {
            ItemTypeText.Text = "🔔 Reminder";
            ItemTypeText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
            ItemTypePill.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0x9F, 0x0A));
            ItemTypePill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x9F, 0x0A));
            NewTaskReminderInput.CaretBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
            AddTaskReminderButton.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
            TaskReminderInputPlaceholder.Text = "+ Add reminder (Press Enter)...";
            if (NewTaskReminderInput.IsFocused)
            {
                TaskReminderInputBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x9F, 0x0A));
            }
        }
        else
        {
            ItemTypeText.Text = "✓ Task";
            ItemTypeText.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            ItemTypePill.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x30, 0xD1, 0x58));
            ItemTypePill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x30, 0xD1, 0x58));
            NewTaskReminderInput.CaretBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            AddTaskReminderButton.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            TaskReminderInputPlaceholder.Text = "+ Add task (Press Enter)...";
            if (NewTaskReminderInput.IsFocused)
            {
                TaskReminderInputBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x30, 0xD1, 0x58));
            }
        }
    }

    public void ShowTaskReminderForDate(DateTime date)
    {
        MonthGridView.Visibility = Visibility.Collapsed;
        AgendaView.Visibility = Visibility.Collapsed;
        TaskReminderView.Visibility = Visibility.Visible;

        TaskReminderDateTitle.Text = date.ToString("MMM d").ToUpperInvariant();
        UpdateItemTypeVisuals();
        RefreshTaskReminderList();

        // Ensure window is activated and focus is assigned for immediate typing
        var win = Window.GetWindow(this);
        if (win != null)
        {
            WindowHelper.EnableActivation(win);
            win.Activate();
        }
        NewTaskReminderInput.Focus();
        Keyboard.Focus(NewTaskReminderInput);
    }

    private void EnsureWindowActivatedAndFocused()
    {
        var win = Window.GetWindow(this);
        if (win != null)
        {
            WindowHelper.EnableActivation(win);
            win.Activate();
        }
        NewTaskReminderInput.Focus();
        Keyboard.Focus(NewTaskReminderInput);
    }

    private void OnTaskReminderInputContainerMouseDown(object sender, MouseButtonEventArgs e)
    {
        EnsureWindowActivatedAndFocused();
        e.Handled = true;
    }

    private void OnTaskReminderInputMouseDown(object sender, MouseButtonEventArgs e)
    {
        EnsureWindowActivatedAndFocused();
    }

    private void OnAddTaskReminderClick(object sender, RoutedEventArgs e)
    {
        AddTaskReminder();
    }

    private void OnNewTaskReminderKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTaskReminder();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            NewTaskReminderInput.Text = "";
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void AddTaskReminder()
    {
        string text = NewTaskReminderInput.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;

        var targetDate = (_selectedDate ?? DateTime.Today).Date;
        var item = new CalendarTaskReminderModel
        {
            Date = targetDate,
            Title = text,
            IsReminder = _isReminderMode,
            IsCompleted = false
        };

        _calendarItems.Add(item);
        SaveCalendarItems();
        RefreshTaskReminderList();

        NewTaskReminderInput.Text = "";
        NewTaskReminderInput.Focus();
    }

    private void RefreshTaskReminderList()
    {
        TaskReminderListPanel.Children.Clear();

        var targetDate = (_selectedDate ?? DateTime.Today).Date;
        var itemsForDate = _calendarItems.Where(i => i.Date.Date == targetDate).ToList();

        if (itemsForDate.Count == 0)
        {
            EmptyTaskReminderPlaceholder.Visibility = Visibility.Visible;
            TaskReminderListPanel.Children.Add(EmptyTaskReminderPlaceholder);
            return;
        }

        EmptyTaskReminderPlaceholder.Visibility = Visibility.Collapsed;

        foreach (var item in itemsForDate)
        {
            var card = CreateTaskReminderCard(item);
            TaskReminderListPanel.Children.Add(card);
        }
    }

    private FrameworkElement CreateTaskReminderCard(CalendarTaskReminderModel item)
    {
        Color primaryAccent = item.IsReminder
            ? Color.FromRgb(0xFF, 0x9F, 0x0A)
            : Color.FromRgb(0x30, 0xD1, 0x58);

        var bgBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x24, primaryAccent.R, primaryAccent.G, primaryAccent.B), 0.0),
                new GradientStop(Color.FromArgb(0x0C, primaryAccent.R, primaryAccent.G, primaryAccent.B), 1.0)
            }
        };

        var borderBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x55, primaryAccent.R, primaryAccent.G, primaryAccent.B), 0.0),
                new GradientStop(Color.FromArgb(0x25, primaryAccent.R, primaryAccent.G, primaryAccent.B), 1.0)
            }
        };

        var container = new Border
        {
            Background = bgBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Glowing vertical indicator bar
        var indicator = new Border
        {
            Width = 2.5,
            Height = 16,
            CornerRadius = new CornerRadius(1.25),
            Background = new SolidColorBrush(primaryAccent),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                Color = primaryAccent,
                BlurRadius = 5,
                ShadowDepth = 0,
                Opacity = 0.55
            }
        };
        Grid.SetColumn(indicator, 0);

        // Checkbox circle
        var checkBtn = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1.2),
            BorderBrush = item.IsCompleted
                ? new SolidColorBrush(primaryAccent)
                : (Brush)FindResource("TextDimBrush"),
            Background = item.IsCompleted
                ? new SolidColorBrush(primaryAccent)
                : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        if (item.IsCompleted)
        {
            checkBtn.Child = new TextBlock
            {
                Text = "✓",
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            };
        }

        checkBtn.MouseLeftButtonUp += (_, _) =>
        {
            item.IsCompleted = !item.IsCompleted;
            SaveCalendarItems();
            RefreshTaskReminderList();
        };
        Grid.SetColumn(checkBtn, 1);

        // Title and Type Pill
        var textStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var badge = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(0x28, primaryAccent.R, primaryAccent.G, primaryAccent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, primaryAccent.R, primaryAccent.G, primaryAccent.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = item.IsReminder ? "🔔 REMINDER" : "✓ TASK",
                FontSize = 7.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(primaryAccent)
            }
        };
        textStack.Children.Add(badge);

        var titleBlock = new TextBlock
        {
            Text = item.Title,
            FontSize = 9.5,
            FontWeight = FontWeights.Medium,
            Foreground = item.IsCompleted
                ? (Brush)FindResource("TextDimBrush")
                : (Brush)FindResource("TextPrimaryBrush"),
            TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null,
            Opacity = item.IsCompleted ? 0.45 : 1.0,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 115,
            VerticalAlignment = VerticalAlignment.Center
        };
        textStack.Children.Add(titleBlock);
        Grid.SetColumn(textStack, 2);

        // Delete button
        var delBtn = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Delete",
            Content = new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            }
        };
        delBtn.Click += (_, _) =>
        {
            _calendarItems.Remove(item);
            SaveCalendarItems();
            RefreshTaskReminderList();
        };
        Grid.SetColumn(delBtn, 3);

        grid.Children.Add(indicator);
        grid.Children.Add(checkBtn);
        grid.Children.Add(textStack);
        grid.Children.Add(delBtn);

        container.Child = grid;
        return container;
    }

    private void ShowAgendaForDate(DateTime date)
    {
        MonthGridView.Visibility = Visibility.Collapsed;
        TaskReminderView.Visibility = Visibility.Collapsed;
        AgendaView.Visibility = Visibility.Visible;

        AgendaDateTitle.Text = date.Date == DateTime.Today
            ? "Today's Agenda"
            : date.ToString("MMM d, yyyy");

        EventsPanel.Children.Clear();

        var events = _calendarService?.TodayEvents.Where(ev => ev.Start.Date == date.Date).ToList() ?? [];
        var customItems = _calendarItems.Where(i => i.Date.Date == date.Date).ToList();

        if (events.Count == 0 && customItems.Count == 0)
        {
            var emptyBorder = new Border
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(4, 8, 4, 0)
            };

            var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            emptyStack.Children.Add(new TextBlock
            {
                Text = "📅  No Upcoming Events",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            emptyStack.Children.Add(new TextBlock
            {
                Text = "Your schedule is clear for this day.",
                FontSize = 8.5,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 6)
            });

            var addBtn = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                Content = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(0x25, 0x30, 0xD1, 0x58)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x30, 0xD1, 0x58)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(9, 3, 9, 3),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock { Text = "+", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)), Margin = new Thickness(0, 0, 3, 0) },
                            new TextBlock { Text = "Add Task / Reminder", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) }
                        }
                    }
                }
            };
            addBtn.Click += (_, _) => ShowTaskReminderForDate(date);
            emptyStack.Children.Add(addBtn);
            emptyBorder.Child = emptyStack;

            EventsPanel.Children.Add(emptyBorder);
            return;
        }

        // Render Custom Tasks & Reminders with Vibrant Apple Tinted Glass Cards
        foreach (var item in customItems)
        {
            Color accentColor = item.IsReminder
                ? Color.FromRgb(0xFF, 0x9F, 0x0A)   // Apple Amber
                : Color.FromRgb(0x30, 0xD1, 0x58);  // Apple Emerald

            var border = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x28, accentColor.R, accentColor.G, accentColor.B),
                    Color.FromArgb(0x10, accentColor.R, accentColor.G, accentColor.B),
                    new Point(0, 0), new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4.5, 8, 4.5),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };
            border.MouseEnter += (_, _) => border.Opacity = 0.86;
            border.MouseLeave += (_, _) => border.Opacity = 1.0;
            border.MouseLeftButtonUp += (_, _) => ShowTaskReminderForDate(date);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new Border
            {
                Width = 3.5,
                Height = 20,
                CornerRadius = new CornerRadius(1.75),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(accentColor),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = accentColor,
                    BlurRadius = 6,
                    ShadowDepth = 0,
                    Opacity = 0.55
                }
            };
            Grid.SetColumn(bar, 0);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = item.IsCompleted ? (Brush)FindResource("TextDimBrush") : (Brush)FindResource("TextPrimaryBrush"),
                TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(0x30, accentColor.R, accentColor.G, accentColor.B)),
                Child = new TextBlock
                {
                    Text = item.IsReminder ? "🔔 Reminder" : (item.IsCompleted ? "✓ Completed Task" : "✓ Task"),
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(
                        (byte)Math.Min(255, accentColor.R + 40),
                        (byte)Math.Min(255, accentColor.G + 40),
                        (byte)Math.Min(255, accentColor.B + 40)))
                }
            };
            textStack.Children.Add(badgeBorder);
            Grid.SetColumn(textStack, 1);

            row.Children.Add(bar);
            row.Children.Add(textStack);
            border.Child = row;
            EventsPanel.Children.Add(border);
        }

        // Render Outlook Events with Apple Calendar Gradient Cards
        foreach (var ev in events)
        {
            Color calColor;
            try
            {
                calColor = (Color)ColorConverter.ConvertFromString(ev.CalendarColor);
            }
            catch
            {
                calColor = Color.FromRgb(0x0A, 0x84, 0xFF); // Apple Classic Blue
            }

            var item = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x28, calColor.R, calColor.G, calColor.B),
                    Color.FromArgb(0x10, calColor.R, calColor.G, calColor.B),
                    new Point(0, 0), new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, calColor.R, calColor.G, calColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4.5, 8, 4.5),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };
            item.MouseEnter += (_, _) => item.Opacity = 0.86;
            item.MouseLeave += (_, _) => item.Opacity = 1.0;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new Border
            {
                Width = 3.5,
                Height = 22,
                CornerRadius = new CornerRadius(1.75),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(calColor),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = calColor,
                    BlurRadius = 6,
                    ShadowDepth = 0,
                    Opacity = 0.55
                }
            };
            Grid.SetColumn(bar, 0);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = ev.Title,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var timeBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(0x30, calColor.R, calColor.G, calColor.B)),
                Child = new TextBlock
                {
                    Text = "🕒 " + ev.TimeDisplay,
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(
                        (byte)Math.Min(255, calColor.R + 60),
                        (byte)Math.Min(255, calColor.G + 60),
                        (byte)Math.Min(255, calColor.B + 60)))
                }
            };
            textStack.Children.Add(timeBadge);
            Grid.SetColumn(textStack, 1);

            row.Children.Add(bar);
            row.Children.Add(textStack);
            item.Child = row;
            EventsPanel.Children.Add(item);
        }
    }
}
