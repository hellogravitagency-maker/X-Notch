using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using XNotch.Helpers;

namespace XNotch.Views;

public partial class ClipboardWidgetView : UserControl
{
    private ClipboardService? _service;
    private string _filter = "All";

    public ClipboardWidgetView() => InitializeComponent();

    public void Bind(ClipboardService service)
    {
        if (_service != null) _service.Changed -= OnChanged;
        _service = service;
        _service.Changed += OnChanged;
        Refresh();
    }

    public void SetEnabled(bool enabled)
    {
        EmptyTitle.Text = enabled ? Loc.T("Nothing copied yet") : Loc.T("Clipboard history is off");
        EmptyHint.Text = enabled
            ? Loc.T("Everything you copy stays here, even after a restart")
            : Loc.T("Turn it on in settings to keep what you copy");
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        _filter = (sender as RadioButton)?.Tag as string ?? "All";
        if (IsLoaded) Refresh();
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => _service?.Clear();

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ListScroll.ScrollToVerticalOffset(Math.Clamp(ListScroll.VerticalOffset - e.Delta / 2.5, 0, ListScroll.ScrollableHeight));
        e.Handled = true;
    }

    private void Refresh()
    {
        if (ListPanel == null) return;
        var items = (_service?.Items ?? []).Where(i => _filter switch
        {
            "All" => true,
            "Pinned" => i.Pinned,
            _ => i.Type.ToString() == _filter,
        }).ToList();

        ListPanel.Children.Clear();
        foreach (var i in items) ListPanel.Children.Add(BuildRow(i));
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildRow(ClipItem item)
    {
        UIElement lead = item.Type == ClipType.Colour && TryParseColour(item.Text, out var c)
            ? new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(c), BorderBrush = (Brush)FindResource("SeparatorBrush"), BorderThickness = new Thickness(1) }
            : new TextBlock
            {
                Text = item.Type switch { ClipType.Link => "🔗", ClipType.Path => "📄", ClipType.Code => "</>", ClipType.Colour => "🎨", _ => "Aa" },
                FontSize = 9.5, FontWeight = FontWeights.Bold, Width = 22, TextAlignment = TextAlignment.Center,
                Foreground = (Brush)FindResource("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center,
            };
        if (lead is FrameworkElement fe) { fe.Margin = new Thickness(0, 0, 8, 0); fe.VerticalAlignment = VerticalAlignment.Center; }

        var preview = new TextBlock
        {
            Text = Preview(item), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = item.Type == ClipType.Code ? new System.Windows.Media.FontFamily("Consolas") : System.Windows.SystemFonts.MessageFontFamily,
            Foreground = (Brush)FindResource("TextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center,
        };

        var pin = SmallButton(item.Pinned ? "★" : "☆", item.Pinned ? Loc.T("Unpin") : Loc.T("Pin"), () => _service?.TogglePin(item));
        var remove = SmallButton("✕", Loc.T("Remove"), () => _service?.Remove(item));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { pin, remove } };
        // Actions appear on hover only (pinned star stays visible so state is readable).
        remove.Opacity = 0;
        pin.Opacity = item.Pinned ? 1 : 0;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(preview, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(lead);
        grid.Children.Add(preview);
        grid.Children.Add(actions);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 4, 4),
            Margin = new Thickness(0, 0, 0, 3), Background = (Brush)FindResource("SurfaceBrush"),
            Cursor = Cursors.Hand, ToolTip = Loc.T("Click to copy · {0}", Age(item.CopiedAt)),
        };
        row.MouseEnter += (_, _) => { remove.Opacity = 1; pin.Opacity = 1; };
        row.MouseLeave += (_, _) => { remove.Opacity = 0; pin.Opacity = item.Pinned ? 1 : 0; };
        // Copying is a dozens-a-day action: instant dim as feedback, no motion.
        row.PreviewMouseLeftButtonDown += (_, _) => row.Opacity = 0.7;
        row.MouseLeftButtonUp += (_, e) =>
        {
            row.Opacity = 1;
            if (e.OriginalSource is DependencyObject d && IsInside(d, actions)) return;
            try { _service?.CopyBack(item); } catch { }
        };
        return row;
    }

    private Button SmallButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Style = (Style)FindResource("IconButton"), Width = 20, Height = 20, Padding = new Thickness(0), ToolTip = tip,
            Content = new TextBlock { Text = glyph, FontSize = 10, Foreground = (Brush)FindResource("TextTertiaryBrush") },
        };
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (var x = d; x != null; x = VisualTreeHelper.GetParent(x)) if (x == container) return true;
        return false;
    }

    private static string Preview(ClipItem item)
    {
        if (item.Type == ClipType.Path)
        {
            var paths = item.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => System.IO.Path.GetFileName(p.Trim().TrimEnd('\\'))).ToArray();
            return paths.Length == 1 ? paths[0] : $"{paths[0]} +{paths.Length - 1}";
        }
        return item.Text.ReplaceLineEndings(" ⏎ ").Trim();
    }

    private static string Age(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? Loc.T("just now") : d.TotalHours < 1 ? Loc.T("{0} min ago", (int)d.TotalMinutes) : d.TotalDays < 1 ? Loc.T("{0} h ago", (int)d.TotalHours) : t.ToString("d MMM");
    }

    private static bool TryParseColour(string text, out Color c)
    {
        c = default;
        try
        {
            var t = text.Trim();
            if (!t.StartsWith('#')) return false; // ponytail: swatch for hex only; rgb()/hsl() show the glyph
            if (t.Length == 4) t = "#" + string.Concat(t.Skip(1).Select(ch => $"{ch}{ch}"));
            c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(t);
            return true;
        }
        catch { return false; }
    }
}
