using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using PasswordBox = System.Windows.Controls.PasswordBox;
using Slider = System.Windows.Controls.Slider;
using FontFamily = System.Windows.Media.FontFamily;
using Panel = System.Windows.Controls.Panel;

namespace XNotch.Views;

/// <summary>
/// macOS System Settings–style window: traffic lights, Mica/Acrylic sidebar with coloured icon
/// rows, grouped inset cards on the right. Every row writes straight to <see cref="AppSettings"/>
/// and the App re-applies (debounced), exactly like the island's inline settings.
/// </summary>
public sealed class SettingsWindow : Window
{
    public const string WindowTitle = "X-Notch Settings";

    private readonly AppSettings _s;
    private readonly ThemeService _theme;
    private readonly Action<AppSettings> _apply;
    private readonly Action _openPlugins;
    private readonly Func<IReadOnlyList<string>> _recentApps;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly StackPanel _nav = new() { Margin = new Thickness(10, 0, 10, 12) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(28, 0, 28, 28) };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(28, 14, 0, 14), VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(Border Row, string Name)> _rows = [];
    private bool _loading;
    private string _page = "Appearance";

    private sealed record Page(string Name, string Glyph, Color Tint, Action<StackPanel> Build);
    private readonly List<Page> _pages;

    public SettingsWindow(AppSettings settings, ThemeService theme, Action<AppSettings> apply, Action openPlugins, Func<IReadOnlyList<string>> recentApps)
    {
        _s = settings;
        _theme = theme;
        _apply = apply;
        _openPlugins = openPlugins;
        _recentApps = recentApps;
        Title = WindowTitle;
        Width = 880; Height = 640; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 48, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(6), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0) });
        foreach (var d in new[] { "ToggleSwitchStyle", "SliderStyle", "PluginSurfaceStyles", "RoundedScrollBarStyle", "IconButtonStyle" })
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/X-Notch;component/Views/Components/{d}.xaml") });

        _debounce.Tick += (_, _) => { _debounce.Stop(); _s.Save(); _apply(_s); };

        _pages =
        [
            new("General", "", Rgb(0x8E, 0x8E, 0x93), General),
            new("Appearance", "", Rgb(0x0A, 0x84, 0xFF), Appearance),
            new("Island", "", Rgb(0x1C, 0x1C, 0x1E), Island),
            new("Dock", "", Rgb(0xFF, 0x9F, 0x0A), DockPage),
            new("Cards", "", Rgb(0xBF, 0x5A, 0xF2), Cards),
            new("Media & HUD", "", Rgb(0xFF, 0x45, 0x3A), Media),
            new("Widgets", "", Rgb(0x32, 0xAD, 0xE6), Widgets),
            new("Clipboard", "", Rgb(0x34, 0xC7, 0x59), Clipboard),
            new("Capture", "", Rgb(0x5E, 0x5C, 0xE6), Capture),
            new("Agents", "", Rgb(0xFF, 0x9F, 0x0A), Agents),
            new("Game Mode", "", Rgb(0x30, 0xD1, 0x58), Game),
            new("Desktop", "", Rgb(0x0A, 0x84, 0xFF), Desktop),
            new("Phone Deck", "", Rgb(0x34, 0xC7, 0x59), Deck),
            new("Streaming", "", Rgb(0xBF, 0x5A, 0xF2), Streaming),
            new("Plugins", "", Rgb(0xFF, 0x9F, 0x0A), Plugins),
            new("About", "", Rgb(0x8E, 0x8E, 0x93), About),
        ];

        Content = BuildShell();
        SourceInitialized += (_, _) => ApplyMaterial();
        _theme.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => _theme.ThemeChanged -= OnThemeChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        ShowPage(_page);
    }

    private void OnThemeChanged() => Dispatcher.BeginInvoke(ApplyMaterial);

    // ───────────────────────── shell ─────────────────────────

    private UIElement BuildShell()
    {
        var root = new Grid();
        root.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        _scroll.SetResourceReference(StyleProperty, "RoundedScrollViewer");
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // Sidebar: transparent, so the window's Mica / Acrylic shows through (macOS vibrancy).
        var side = new DockPanel();
        var lights = TrafficLights();
        DockPanel.SetDock(lights, Dock.Top);
        side.Children.Add(lights);
        var search = new TextBox { Margin = new Thickness(10, 6, 10, 10), Height = 30, Tag = Loc.T("Search") };
        search.SetResourceReference(StyleProperty, "PluginSearchTextBox");
        search.TextChanged += (_, _) => Filter(search.Text);
        DockPanel.SetDock(search, Dock.Top);
        side.Children.Add(search);
        side.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        foreach (var p in _pages) _nav.Children.Add(NavRow(p));
        root.Children.Add(side);

        var main = new DockPanel();
        main.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
        var header = new Grid { Height = 52 };
        header.Children.Add(_title);
        DockPanel.SetDock(header, Dock.Top);
        main.Children.Add(header);
        main.Children.Add(_scroll);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        // Hairline between sidebar and content.
        var line = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left };
        line.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
        Grid.SetColumn(line, 1);
        root.Children.Add(line);
        return root;
    }

    private FrameworkElement TrafficLights()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 18, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        row.Children.Add(Light(Rgb(0xFF, 0x5F, 0x57), "", Close));
        row.Children.Add(Light(Rgb(0xFE, 0xBC, 0x2E), "", () => WindowState = WindowState.Minimized)); // genie takes it to the dock
        row.Children.Add(Light(Rgb(0x28, 0xC8, 0x40), "", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        // Glyphs show on hover over the group, like macOS.
        row.MouseEnter += (_, _) => { foreach (var g in row.Children.OfType<Border>()) g.Child.Opacity = 1; };
        row.MouseLeave += (_, _) => { foreach (var g in row.Children.OfType<Border>()) g.Child.Opacity = 0; };
        return row;
    }

    private static Border Light(Color c, string glyph, Action click)
    {
        var b = new Border
        {
            Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderThickness = new Thickness(0.5),
            Child = new TextBlock { Text = glyph, FontSize = 7, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 },
        };
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.PreviewMouseLeftButtonDown += (_, e) => { b.Opacity = 0.7; e.Handled = true; };
        b.MouseLeftButtonUp += (_, _) => { b.Opacity = 1; click(); };
        b.MouseLeave += (_, _) => b.Opacity = 1;
        return b;
    }

    private Border NavRow(Page p)
    {
        var icon = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0),
            Background = new LinearGradientBrush(Lighten(p.Tint, 0.18), p.Tint, 90),
            Child = new TextBlock { Text = p.Glyph, FontSize = 12, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var label = new TextBlock { Text = Loc.T(p.Name), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var row = new Border
        {
            Height = 32, CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 1, 0, 1),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = p.Name,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
        };
        row.MouseLeftButtonUp += (_, _) => ShowPage(p.Name);
        _rows.Add((row, p.Name));
        return row;
    }

    private void Filter(string q)
    {
        foreach (var (row, name) in _rows)
            row.Visibility = q.Length == 0 || Loc.T(name).Contains(q, StringComparison.OrdinalIgnoreCase) || name.Contains(q, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowPage(string name)
    {
        _page = name;
        var page = _pages.First(p => p.Name == name);
        foreach (var (row, n) in _rows)
        {
            bool on = n == name;
            if (on) row.SetResourceReference(Border.BackgroundProperty, "SidebarSelectedBrush"); else row.Background = Brushes.Transparent;
            if (((StackPanel)row.Child).Children[1] is TextBlock t)
            {
                if (on) t.Foreground = Brushes.White; else t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            }
        }
        _title.Text = Loc.T(page.Name);
        var body = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch };
        _loading = true;
        page.Build(body);
        _loading = false;
        _scroll.Content = body;
        _scroll.ScrollToTop();
        // Page swap: quick fade + 6 px rise (occasional action, keep it under 200 ms).
        if (SystemParameters.ClientAreaAnimation)
        {
            body.RenderTransform = new TranslateTransform(0, 6);
            body.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            body.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            body.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        }
    }

    /// <summary>Mica for solid themes, Acrylic for the glass themes; dark title bar follows the palette.</summary>
    private void ApplyMaterial()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = _theme.CurrentPalette.IsLight ? 0 : 1;
        DwmSetWindowAttribute(h, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(h, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
        int kind = _theme.UsesBackdrop ? 3 /* Acrylic */ : 2 /* Mica */;
        bool ok = DwmSetWindowAttribute(h, 38 /* DWMWA_SYSTEMBACKDROP_TYPE */, ref kind, 4) == 0;
        // No system backdrop (Windows 10 / old 11): paint the sidebar solid instead of see-through black.
        if (ok) Background = Brushes.Transparent; else SetResourceReference(BackgroundProperty, "SurfaceBrush");
    }

    // ───────────────────────── row builders ─────────────────────────

    private void Changed() { if (_loading) return; _debounce.Stop(); _debounce.Start(); }

    private static void Header(StackPanel body, string text, string? note = null)
    {
        var t = new TextBlock { Text = Loc.T(text), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 18, 0, 6) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        body.Children.Add(t);
        if (note != null)
        {
            var n = new TextBlock { Text = Loc.T(note), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, -2, 0, 8) };
            n.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            body.Children.Add(n);
        }
    }

    /// <summary>Inset grouped list (rounded card, hairline separators inset past the label).</summary>
    private static StackPanel Group(StackPanel body)
    {
        var stack = new StackPanel();
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = stack, Margin = new Thickness(0, 0, 0, 4) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        body.Children.Add(card);
        return stack;
    }

    private static void Row(StackPanel group, string label, FrameworkElement control, string? help = null)
    {
        if (group.Children.Count > 0)
        {
            var sep = new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) };
            sep.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
            group.Children.Add(sep);
        }
        var grid = new Grid { MinHeight = 44, Margin = new Thickness(14, 6, 12, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = Loc.T(label), FontSize = 13, TextWrapping = TextWrapping.Wrap });
        if (help != null)
        {
            var h = new TextBlock { Text = Loc.T(help), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            text.Children.Add(h);
        }
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        group.Children.Add(grid);
    }

    private void Toggle(StackPanel g, string label, Func<bool> get, Action<bool> set, string? help = null)
    {
        var c = new CheckBox { IsChecked = get() };
        c.SetResourceReference(StyleProperty, "ToggleSwitch");
        c.Click += (_, _) => { set(c.IsChecked == true); Changed(); };
        Row(g, label, c, help);
    }

    private void Range(StackPanel g, string label, double min, double max, double step, Func<double> get, Action<double> set, Func<double, string> fmt, string? help = null)
    {
        var value = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Text = fmt(get()) };
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var s = new Slider { Minimum = min, Maximum = max, Value = get(), Width = 190, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        s.SetResourceReference(StyleProperty, "CompactSlider");
        s.ValueChanged += (_, e) => { set(e.NewValue); value.Text = fmt(e.NewValue); Changed(); };
        Row(g, label, new StackPanel { Orientation = Orientation.Horizontal, Children = { s, value } }, help);
    }

    private void Choice<T>(StackPanel g, string label, (T Value, string Name)[] options, Func<T> get, Action<T> set, string? help = null)
    {
        var box = new ComboBox { MinWidth = 170 };
        box.SetResourceReference(StyleProperty, "PluginFieldComboBox");
        foreach (var o in options) box.Items.Add(new ComboBoxItem { Content = Loc.T(o.Name), Tag = o.Value });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, get())) ?? box.Items[0];
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: T v }) { set(v); Changed(); } };
        Row(g, label, box, help);
    }

    private void Text(StackPanel g, string label, Func<string?> get, Action<string?> set, string? help = null, double width = 200)
    {
        var box = new TextBox { Text = get() ?? "", Width = width, Height = 28 };
        box.SetResourceReference(StyleProperty, "PluginFieldTextBox");
        void Commit() { var v = box.Text.Trim(); set(v.Length == 0 ? null : v); Changed(); }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        Row(g, label, box, help);
    }

    private void Secret(StackPanel g, string label, Action<string> set, string? help = null)
    {
        var box = new PasswordBox { Width = 200, Height = 28, Padding = new Thickness(8, 4, 8, 4) };
        box.LostFocus += (_, _) => { if (box.Password.Length > 0) { set(box.Password); box.Clear(); Changed(); } };
        Row(g, label, box, help ?? "Saved encrypted for this Windows user. Leave empty to keep the saved one.");
    }

    private static void ActionRow(StackPanel g, string label, string button, Action click, string? help = null)
    {
        var b = new Button { Content = Loc.T(button), Padding = new Thickness(14, 5, 14, 5) };
        b.SetResourceReference(StyleProperty, "PluginSecondaryButton");
        b.Click += (_, _) => click();
        Row(g, label, b, help);
    }

    private static void Info(StackPanel g, string label, string value)
    {
        var t = new TextBlock { Text = value, FontSize = 12.5 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Row(g, label, t);
    }

    // ───────────────────────── pages ─────────────────────────

    private void General(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "Open at login", () => _s.StartOnBoot, v => { _s.StartOnBoot = v; _s.ApplyStartOnBoot(); });
        Toggle(g, "Check for updates automatically", () => _s.AutoCheckForUpdates, v => _s.AutoCheckForUpdates = v);
        var langs = new List<(string, string)> { ("auto", "Automatic") };
        foreach (var (code, name) in Loc.Languages) langs.Add((code, name));
        Choice(g, "Language", langs.ToArray(), () => _s.Language, v => _s.Language = v, "Applies the next time X-Notch starts.");

        Header(b, "Opening the island");
        g = Group(b);
        Choice(g, "Hover behaviour", [(HoverMode.HoverPeekClickOpen, "Peek on hover, click to open"), (HoverMode.LongHoverOpen, "Open on long hover")], () => _s.HoverMode, v => _s.HoverMode = v);
        Range(g, "Hover delay", 0, 600, 10, () => _s.HoverOpenDelayMs, v => _s.HoverOpenDelayMs = (int)v, v => $"{v:0} ms");
        Range(g, "Close delay", 0, 1000, 10, () => _s.HoverCloseDelayMs, v => _s.HoverCloseDelayMs = (int)v, v => $"{v:0} ms");
        Range(g, "Long hover", 200, 1500, 50, () => _s.LongHoverDelayMs, v => _s.LongHoverDelayMs = (int)v, v => $"{v:0} ms");
    }

    private void Appearance(StackPanel b)
    {
        Header(b, "Theme");
        var themes = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (t, name) in new[] { (AppTheme.Auto, "Auto"), (AppTheme.Light, "Light"), (AppTheme.Dark, "Dark"), (AppTheme.LiquidGlass, "Liquid Glass"), (AppTheme.Glass, "Glass"), (AppTheme.Graphite, "Graphite"), (AppTheme.Oled, "OLED Black") })
            themes.Children.Add(ThemeTile(t, name));
        b.Children.Add(themes);

        Header(b, "Accent colour");
        var accents = new WrapPanel();
        foreach (var (hex, name) in new (string?, string)[] { (null, "Windows"), ("#007AFF", "Blue"), ("#AF52DE", "Purple"), ("#FF2D55", "Pink"), ("#FF3B30", "Red"), ("#FF9500", "Orange"), ("#FFCC00", "Yellow"), ("#28CD41", "Green"), ("#8E8E93", "Graphite") })
            accents.Children.Add(AccentDot(hex, name));
        b.Children.Add(new Border { Child = accents, Padding = new Thickness(4, 0, 0, 0) });

        Header(b, "Island shape");
        var g = Group(b);
        Range(g, "Corner roundness", 0.5, 1.6, 0.05, () => _s.CornerRadiusScale, v => _s.CornerRadiusScale = v, v => $"{v:0.00}×");
        Toggle(g, "Shadow", () => _s.ShowShadow, v => _s.ShowShadow = v, "A soft shadow under the island, like the iPhone Dynamic Island.");
        Toggle(g, "Album art glow", () => _s.ShowAlbumGlow, v => _s.ShowAlbumGlow = v, "A soft halo in the album's colour while music plays.");

        Header(b, "Motion");
        g = Group(b);
        Range(g, "Animation speed", 0.5, 2.0, 0.05, () => _s.AnimationSpeed, v => _s.AnimationSpeed = v, v => $"{v:0.00}×");
        Range(g, "Springiness", 0, 1.5, 0.05, () => _s.SpringIntensity, v => _s.SpringIntensity = v, v => v < 0.05 ? "None" : $"{v:0.00}×");
        Toggle(g, "Reduce motion", () => _s.ReduceMotion, v => _s.ReduceMotion = v, "No bounce, short transitions. Also on when Windows animation effects are off.");
    }

    private FrameworkElement ThemeTile(AppTheme t, string name)
    {
        bool selected = t == AppTheme.Auto ? _s.AdaptToWindowsTheme : !_s.AdaptToWindowsTheme && _s.GetManualTheme() == t;
        var preview = new Grid { Width = 96, Height = 62, ClipToBounds = true };
        preview.Children.Add(new Border { Background = TileBackground(t) });
        // A tiny island + dock on each preview.
        var (fill, rim) = t switch
        {
            AppTheme.Light => (Rgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0x30, 0, 0, 0)),
            AppTheme.LiquidGlass => (Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            AppTheme.Glass => (Color.FromArgb(0x90, 0x16, 0x16, 0x1A), Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            AppTheme.Graphite => (Rgb(0x23, 0x23, 0x26), Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            AppTheme.Oled => (Colors.Black, Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            _ => (Colors.Black, Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
        };
        preview.Children.Add(new Border { Width = 40, Height = 11, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), Background = new SolidColorBrush(fill), BorderBrush = new SolidColorBrush(rim), BorderThickness = new Thickness(0.8) });
        preview.Children.Add(new Border { Width = 58, Height = 10, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5), Background = new SolidColorBrush(Color.FromArgb(0x90, fill.R, fill.G, fill.B)), BorderBrush = new SolidColorBrush(rim), BorderThickness = new Thickness(0.8) });

        var frame = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(selected ? 2.5 : 1), Padding = new Thickness(2), Child = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = preview } };
        if (selected) frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); else frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var label = new TextBlock { Text = Loc.T(name), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
        var tile = new StackPanel { Margin = new Thickness(0, 0, 14, 12), Cursor = Cursors.Hand, Background = Brushes.Transparent, Children = { frame, label } };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            _s.AdaptToWindowsTheme = t == AppTheme.Auto;
            _s.Theme = t == AppTheme.Auto ? AppTheme.Dark : t;
            _s.UseLiquidGlassTheme = t == AppTheme.LiquidGlass;
            _s.Save();
            _apply(_s); // instant: the user is looking at the result
            ShowPage(_page);
        };
        return tile;
    }

    private static System.Windows.Media.Brush TileBackground(AppTheme t) => t switch
    {
        AppTheme.Auto => new LinearGradientBrush(new GradientStopCollection { new(Rgb(0xE8, 0xEC, 0xF4), 0.5), new(Rgb(0x2A, 0x2C, 0x34), 0.5) }, new Point(0, 0), new Point(1, 0)),
        AppTheme.Light => new LinearGradientBrush(Rgb(0xDD, 0xE6, 0xF5), Rgb(0xF5, 0xF0, 0xE8), 45),
        AppTheme.Dark => new LinearGradientBrush(Rgb(0x2C, 0x2E, 0x3A), Rgb(0x15, 0x16, 0x1C), 45),
        AppTheme.LiquidGlass or AppTheme.Glass => new LinearGradientBrush(new GradientStopCollection { new(Rgb(0x3A, 0x7B, 0xFF), 0), new(Rgb(0xB0, 0x4C, 0xF0), 0.55), new(Rgb(0xFF, 0x7A, 0x59), 1) }, 45),
        AppTheme.Graphite => new LinearGradientBrush(Rgb(0x4A, 0x4A, 0x50), Rgb(0x2A, 0x2A, 0x2E), 45),
        _ => new SolidColorBrush(Rgb(0x05, 0x05, 0x06)),
    };

    private FrameworkElement AccentDot(string? hex, string name)
    {
        bool selected = string.Equals(_s.AccentColor, hex, StringComparison.OrdinalIgnoreCase);
        System.Windows.Media.Brush fill = hex == null
            ? new LinearGradientBrush(new GradientStopCollection { new(Rgb(0xFF, 0x3B, 0x30), 0), new(Rgb(0xFF, 0xCC, 0x00), 0.33), new(Rgb(0x28, 0xCD, 0x41), 0.66), new(Rgb(0x00, 0x7A, 0xFF), 1) }, 45)
            : new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = fill, BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderThickness = new Thickness(0.5) };
        if (selected) dot.Child = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Brushes.White };
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = Loc.T(name), Children = { dot } };
        var label = new TextBlock { Text = Loc.T(name), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Opacity = selected ? 1 : 0.0 };
        stack.Children.Add(label);
        // Press feedback only; the accent re-themes the whole app, which is feedback enough.
        stack.PreviewMouseLeftButtonDown += (_, _) => dot.RenderTransform = new ScaleTransform(0.92, 0.92, 11, 11);
        stack.MouseLeave += (_, _) => dot.RenderTransform = null;
        stack.MouseLeftButtonUp += (_, _) =>
        {
            dot.RenderTransform = null;
            _s.AccentColor = hex;
            _s.Save();
            _apply(_s);
            ShowPage(_page);
        };
        return stack;
    }

    private void Island(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "Auto-hide when idle", () => _s.AutoHide, v => _s.AutoHide = v, "Tucks the island into a thin sliver until you point at it.");
        Toggle(g, "Pop up under the camera", () => _s.CameraHoverPopUp, v => _s.CameraHoverPopUp = v, "Briefly shows the island when the pointer rests under the webcam.");
        Toggle(g, "Open at the cursor", () => _s.CursorCenterPlacement, v => _s.CursorCenterPlacement = v);
        Toggle(g, "Reserve space at the top", () => _s.ReserveSpaceAtTop, v => _s.ReserveSpaceAtTop = v, "Maximized windows start below the island, like a menu bar.");

        Header(b, "Displays");
        g = Group(b);
        Toggle(g, "Show on every display", () => _s.ShowOnAllScreens, v => _s.ShowOnAllScreens = v);
        Toggle(g, "Open and close together", () => _s.SyncIslandsOpenState, v => _s.SyncIslandsOpenState = v);
        Choice(g, "When an app is fullscreen", [(AppRule.Hide, "Hide the island"), (AppRule.Slim, "Slim island"), (AppRule.Game, "Game strip"), (AppRule.Normal, "Keep it")], () => _s.FullscreenRule, v => _s.FullscreenRule = v);
    }

    private void DockPage(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "Use the X-Notch dock", () => _s.DockEnabled, v => _s.DockEnabled = v, "Replaces the Windows taskbar while on. The taskbar comes back when you turn it off or quit.");
        Toggle(g, "Automatically hide and show the dock", () => _s.DockAutoHide, v => _s.DockAutoHide = v);
        Range(g, "Size", 32, 80, 2, () => _s.DockIconSize, v => _s.DockIconSize = v, v => $"{v:0}");
        Toggle(g, "Magnification", () => _s.DockMagnification, v => _s.DockMagnification = v);
        Range(g, "Magnified size", 1.1, 2.2, 0.05, () => _s.DockMagnifiedScale, v => _s.DockMagnifiedScale = v, v => $"{v:0.00}×");

        Header(b, "Windows");
        g = Group(b);
        Toggle(g, "Genie effect", () => _s.GenieEffect, v => _s.GenieEffect = v, "X-Notch windows pour into the dock when minimized.");
        Toggle(g, "Genie for every app", () => _s.GenieAllWindows, v => _s.GenieAllWindows = v, "Any window you minimize funnels into its dock icon, and pours back out when you click the icon. For the genie alone, untick \"Animate windows when minimizing and maximizing\" in Windows' Performance Options (SystemPropertiesPerformance).");

        Header(b, "Displays");
        g = Group(b);
        Toggle(g, "Dock on every display", () => _s.DockEveryScreen, v => _s.DockEveryScreen = v);
        Toggle(g, "Show only apps on that display", () => _s.DockAppsOnOwnScreen, v => _s.DockAppsOnOwnScreen = v);
    }

    private void Cards(StackPanel b)
    {
        Header(b, "Cards in the open island", "Swipe or use the arrows to move between them.");
        var g = Group(b);
        void C(string n, Func<bool> get, Action<bool> set) => Toggle(g, n, get, set);
        C("Music", () => _s.ShowMusicCard, v => _s.ShowMusicCard = v);
        C("Widgets", () => _s.ShowWidgetsCard, v => _s.ShowWidgetsCard = v);
        C("Controls", () => _s.ShowControlsCard, v => _s.ShowControlsCard = v);
        C("Clipboard", () => _s.ShowClipboardCard, v => _s.ShowClipboardCard = v);
        C("Capture", () => _s.ShowCaptureCard, v => _s.ShowCaptureCard = v);
        C("Notifications", () => _s.ShowNotificationsCard, v => _s.ShowNotificationsCard = v);
        C("Agents", () => _s.ShowAgentsCard, v => _s.ShowAgentsCard = v);
        C("Phone Deck", () => _s.ShowDeckCard, v => _s.ShowDeckCard = v);
        C("Streaming", () => _s.ShowStreamCard, v => _s.ShowStreamCard = v);
        C("Tasks", () => _s.ShowTasksCard, v => _s.ShowTasksCard = v);
        C("System vitals", () => _s.ShowVitalsCard, v => _s.ShowVitalsCard = v);
        C("Stopwatch", () => _s.ShowStopwatchCard, v => _s.ShowStopwatchCard = v);
        C("Water", () => _s.ShowWaterCard, v => _s.ShowWaterCard = v);
        C("Sleep", () => _s.ShowSleepCard, v => _s.ShowSleepCard = v);
    }

    private void Media(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "Now Playing in the island", () => _s.ShowMusicControls, v => _s.ShowMusicControls = v);
        Toggle(g, "Audio visualizer", () => _s.ShowVisualizer, v => _s.ShowVisualizer = v);
        Header(b, "HUD");
        g = Group(b);
        Toggle(g, "Volume", () => _s.ShowVolumeHud, v => _s.ShowVolumeHud = v, "Replaces the Windows volume flyout.");
        Toggle(g, "Brightness", () => _s.ShowBrightnessHud, v => _s.ShowBrightnessHud = v);
        Toggle(g, "Battery", () => _s.ShowBattery, v => _s.ShowBattery = v);
        Toggle(g, "Calendar", () => _s.ShowCalendar, v => _s.ShowCalendar = v);
        Header(b, "Camera");
        g = Group(b);
        Toggle(g, "Mirror preview", () => _s.ShowWebcam, v => _s.ShowWebcam = v);
        Range(g, "Frame rate", 5, 60, 1, () => _s.WebcamFps, v => _s.WebcamFps = (int)v, v => $"{v:0} fps");
    }

    private void Widgets(StackPanel b)
    {
        Header(b, "Weather", "Only the city you type is sent to Open-Meteo.");
        var g = Group(b);
        Text(g, "City", () => _s.WeatherCity, v => { _s.WeatherCity = v; _s.WeatherLatitude = _s.WeatherLongitude = null; });
        Toggle(g, "Fahrenheit", () => _s.WeatherFahrenheit, v => _s.WeatherFahrenheit = v);
        Header(b, "Tiles");
        g = Group(b);
        Text(g, "Currency pair", () => _s.CurrencyPair, v => _s.CurrencyPair = v ?? "USD/EUR", "For example USD/EUR (ECB rates).", 120);
        Text(g, "World clock", () => _s.WorldClockZone, v => _s.WorldClockZone = v ?? "UTC", "Time zone id, for example Asia/Tokyo.", 160);
        Text(g, "Ping host", () => _s.PingHost, v => _s.PingHost = v ?? "1.1.1.1", null, 160);
        Text(g, "Countdown title", () => _s.CountdownTitle, v => _s.CountdownTitle = v);
        Text(g, "Countdown date", () => _s.CountdownDate, v => _s.CountdownDate = v, "yyyy-mm-dd", 120);
        Choice(g, "Pet", Enum.GetValues<PetKind>().Select(p => (p, p.ToString())).ToArray(), () => _s.PetKind, v => _s.PetKind = v);
        Header(b, "Focus timer");
        g = Group(b);
        Range(g, "Focus", 5, 90, 5, () => _s.FocusMinutes, v => _s.FocusMinutes = (int)v, v => $"{v:0} min");
        Range(g, "Break", 1, 30, 1, () => _s.BreakMinutes, v => _s.BreakMinutes = (int)v, v => $"{v:0} min");
    }

    private void Clipboard(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "Keep clipboard history", () => _s.ClipboardEnabled, v => _s.ClipboardEnabled = v, "Encrypted on disk. Copies from password managers are never kept.");
        Range(g, "Keep for", 1, 168, 1, () => _s.ClipboardKeepHours, v => _s.ClipboardKeepHours = (int)v, v => $"{v:0} h");
        Range(g, "Items", 10, 200, 10, () => _s.ClipboardHistorySize, v => _s.ClipboardHistorySize = (int)v, v => $"{v:0}");
    }

    private void Capture(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "New screenshots go to the shelf", () => _s.CapturesToShelf, v => _s.CapturesToShelf = v, "Screenshots and recordings saved by Snipping Tool land on the island's shelf.");
    }

    private void Agents(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "Show Claude Code and Codex status", () => _s.AgentsEnabled, v => _s.AgentsEnabled = v, "Turning this on installs small reporting hooks (your existing config is backed up first).");
    }

    private void Game(StackPanel b)
    {
        Header(b, "Per-app behaviour", "Normal, a slim island, hidden, or the game strip with FPS and temperatures.");
        var g = Group(b);
        var apps = _recentApps().Concat(_s.AppRules.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList();
        if (apps.Count == 0) Info(g, "Switch to an app and it appears here", "");
        foreach (var exe in apps)
            Choice(g, exe, Enum.GetValues<AppRule>().Select(r => (r, r.ToString())).ToArray(),
                () => _s.AppRules.TryGetValue(exe, out var r) ? r : AppRule.Normal,
                v => { if (v == AppRule.Normal) _s.AppRules.Remove(exe); else _s.AppRules[exe] = v; });
    }

    private void Desktop(StackPanel b)
    {
        Header(b, "Live wallpaper");
        var g = Group(b);
        Toggle(g, "Video wallpaper", () => _s.LiveWallpaper, v => _s.LiveWallpaper = v, "Plays behind the desktop icons and pauses when a window covers it.");
        ActionRow(g, _s.LiveWallpaperFile is { } f ? System.IO.Path.GetFileName(f) : Loc.T("No video chosen"), "Choose…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Videos|*.mp4;*.wmv;*.mov;*.m4v|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            _s.LiveWallpaperFile = dlg.FileName;
            _s.LiveWallpaper = true;
            Changed();
            ShowPage(_page);
        });
        Range(g, "Brightness", 0.2, 1, 0.05, () => _s.LiveWallpaperBrightness, v => _s.LiveWallpaperBrightness = v, v => $"{v * 100:0}%");
        Header(b, "Window switcher");
        g = Group(b);
        Toggle(g, "Replace Alt-Tab", () => _s.AltTabEnabled, v => _s.AltTabEnabled = v, "Live thumbnails, no animation, Esc to cancel.");
        Header(b, "Projects");
        g = Group(b);
        foreach (var root in _s.WorkspaceRoots.ToList())
            ActionRow(g, root, "Remove", () => { _s.WorkspaceRoots.Remove(root); Changed(); ShowPage(_page); });
        ActionRow(g, "Workspace folders", "Add…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Select Workspace Folder to Scan for Projects") };
            if (dlg.ShowDialog(this) != true || _s.WorkspaceRoots.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase)) return;
            _s.WorkspaceRoots.Add(dlg.FolderName);
            Changed();
            ShowPage(_page);
        }, "Folders scanned for projects and tasks.");
    }

    private void Deck(StackPanel b)
    {
        var g = Group(b);
        Toggle(g, "Phone Deck", () => _s.DeckEnabled, v => _s.DeckEnabled = v, "A remote for your phone on the local network. Never runs on Public networks. Pair from the Deck card in the island.");
        Info(g, "Paired phones", _s.DeckDevices.Count.ToString());
    }

    private void Streaming(StackPanel b)
    {
        Header(b, "Chat");
        var g = Group(b);
        Text(g, "Twitch channel", () => _s.ChatTwitch, v => _s.ChatTwitch = v);
        Text(g, "YouTube video id", () => _s.ChatYouTubeVideo, v => _s.ChatYouTubeVideo = v);
        Secret(g, "YouTube API key", v => _s.ChatYouTubeKeyProtected = AppSettings.Protect(v));
        Toggle(g, "Chat overlay", () => _s.ChatOverlay, v => _s.ChatOverlay = v, "Click-through chat on top of your game.");
        Header(b, "OBS");
        g = Group(b);
        Toggle(g, "Connect to OBS", () => _s.ObsEnabled, v => _s.ObsEnabled = v, "obs-websocket v5 on this PC.");
        Text(g, "Port", () => _s.ObsPort.ToString(), v => { if (int.TryParse(v, out var p) && p is > 0 and < 65536) _s.ObsPort = p; }, null, 90);
        Secret(g, "Password", v => _s.ObsPasswordProtected = AppSettings.Protect(v));
    }

    private void Plugins(StackPanel b)
    {
        var g = Group(b);
        ActionRow(g, "Installed and available plugins", "Manage…", _openPlugins);
    }

    private void About(StackPanel b)
    {
        var g = Group(b);
        Info(g, "Version", AppInfo.NameWithVersion);
        Info(g, "Settings file", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X-Notch", "settings.json"));
    }

    // ───────────────────────── helpers ─────────────────────────

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Lighten(Color c, double t) => Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
}
