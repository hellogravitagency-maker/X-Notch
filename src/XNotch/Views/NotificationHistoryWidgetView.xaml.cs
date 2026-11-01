using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace XNotch.Views;

public partial class NotificationHistoryWidgetView : UserControl
{
    private NotificationService? _service;

    public NotificationHistoryWidgetView()
    {
        InitializeComponent();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (NotificationScrollViewer != null)
        {
            double targetOffset = NotificationScrollViewer.VerticalOffset - (e.Delta / 2.5);
            NotificationScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, NotificationScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }

    public void Bind(NotificationService service)
    {
        if (_service != null)
        {
            _service.NotificationArrived -= OnNotificationArrived;
            _service.NotificationDismissed -= OnNotificationDismissed;
        }

        _service = service;
        _service.NotificationArrived += OnNotificationArrived;
        _service.NotificationDismissed += OnNotificationDismissed;

        RefreshList();
    }

    private void OnNotificationArrived(NotificationItem item)
    {
        Dispatcher.Invoke(RefreshList);
    }

    private void OnNotificationDismissed(uint id)
    {
        Dispatcher.Invoke(RefreshList);
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        _service?.ClearHistory();
        RefreshList();
    }

    public void RefreshList()
    {
        NotificationListPanel.Children.Clear();

        var history = _service?.History ?? [];
        NotificationCountText.Text = history.Count == 1 ? "1 alert" : $"{history.Count} alerts";

        if (history.Count == 0)
        {
            EmptyPlaceholder.Visibility = Visibility.Visible;
            NotificationListPanel.Children.Add(EmptyPlaceholder);
            return;
        }

        EmptyPlaceholder.Visibility = Visibility.Collapsed;

        foreach (var item in history)
        {
            var visual = NotificationVisualResolver.Resolve(item);
            var card = CreateNotificationCard(item, visual);
            NotificationListPanel.Children.Add(card);
        }
    }

    private FrameworkElement CreateNotificationCard(NotificationItem item, NotificationVisualInfo visual)
    {
        var container = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 3),
            Cursor = Cursors.Hand,
            ToolTip = $"Click to open {item.AppName}"
        };

        container.MouseLeftButtonUp += (_, e) =>
        {
            AppLauncherHelper.LaunchApplicationForNotification(item);
            e.Handled = true;
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // App Icon / Squircle badge
        var iconBorder = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6.5),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = visual.AppLogoSource != null ? (Brush)FindResource("SurfaceRaisedBrush") : visual.BackgroundBrush
        };

        if (visual.AppLogoSource != null)
        {
            iconBorder.Child = new Image
            {
                Source = visual.AppLogoSource,
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            iconBorder.Child = new TextBlock
            {
                Text = visual.Glyph,
                FontSize = 12,
                Foreground = visual.ForegroundBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconBorder, 0);

        // Texts stack
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

        // Subheader: AppName + relative time
        var subHeader = new StackPanel { Orientation = Orientation.Horizontal };
        subHeader.Children.Add(new TextBlock
        {
            Text = item.AppName.ToUpperInvariant(),
            FontSize = 8.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        });

        string timeAgo = FormatRelativeTime(item.Timestamp);
        subHeader.Children.Add(new TextBlock
        {
            Text = $" • {timeAgo}",
            FontSize = 8.5,
            Foreground = (Brush)FindResource("TextFaintBrush")
        });
        textStack.Children.Add(subHeader);

        // Title
        textStack.Children.Add(new TextBlock
        {
            Text = visual.Title,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0)
        });

        // Body (if present)
        if (!string.IsNullOrWhiteSpace(visual.Body))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = visual.Body,
                FontSize = 9.0,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }
        Grid.SetColumn(textStack, 1);

        // Dismiss (✕) button
        var dismissBtn = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Width = 18,
            Height = 18,
            Cursor = Cursors.Hand,
            ToolTip = "Dismiss notification",
            Content = new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        uint itemId = item.Id;
        dismissBtn.Click += (_, e) =>
        {
            e.Handled = true;
            _service?.DismissNotification(itemId);
            RefreshList();
        };
        Grid.SetColumn(dismissBtn, 2);

        grid.Children.Add(iconBorder);
        grid.Children.Add(textStack);
        grid.Children.Add(dismissBtn);

        container.Child = grid;
        return container;
    }

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;
        if (elapsed.TotalSeconds < 30) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return timestamp.ToLocalTime().ToString("MMM d");
    }
}
