using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;
using XNotch.Helpers;

namespace XNotch.Views;

/// <summary>Control Center-style quick toggles plus audio device pickers.</summary>
public partial class ControlsWidgetView : UserControl
{
    private static readonly Color Accent = Color.FromRgb(0x0A, 0x84, 0xFF);

    private SystemControlsService? _controls;
    private VolumeService? _volume;
    private BatteryService? _battery;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Collections.Generic.List<(Border Tile, Func<bool?> State)> _tiles = [];
    private Button? _output, _input;
    private TextBlock? _batteryText;

    public ControlsWidgetView()
    {
        InitializeComponent();
        // ponytail: mic mute has no change event in NAudio; poll only while the card is on screen.
        _poll.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _poll.Start(); } else _poll.Stop(); };
    }

    public void Bind(SystemControlsService controls, VolumeService volume, BatteryService battery)
    {
        _controls = controls;
        _volume = volume;
        _battery = battery;
        controls.Changed += () => Dispatcher.BeginInvoke(Refresh);
        volume.VolumeChanged += (_, _) => Dispatcher.BeginInvoke(Refresh);
        Build();
        Refresh();
    }

    private void Build()
    {
        TileRow.Children.Clear();
        _tiles.Clear();
        var c = _controls!;

        AddTile("", Loc.T("Wi-Fi"), () => c.HasWifi ? c.WifiOn : null, async () => await c.SetWifiAsync(!c.WifiOn), "ms-settings:network-wifi");
        AddTile("", Loc.T("Bluetooth"), () => c.HasBluetooth ? c.BluetoothOn : null, async () => await c.SetBluetoothAsync(!c.BluetoothOn), "ms-settings:bluetooth");
        AddTile("", Loc.T("Focus"), () => c.HasFocus ? c.FocusOn : null, () => { c.SetFocus(!c.FocusOn); return System.Threading.Tasks.Task.CompletedTask; }, "ms-settings:focus");
        AddTile("", Loc.T("Night light"), () => false, () => { SystemControlsService.OpenNightLight(); return System.Threading.Tasks.Task.CompletedTask; }, null);
        AddTile("", Loc.T("Sound"), () => !_volume!.IsMuted, () => { _volume!.ToggleMute(); return System.Threading.Tasks.Task.CompletedTask; }, "ms-settings:sound");
        AddTile("", Loc.T("Mic"), () => !c.MicMuted, () => { c.MicMuted = !c.MicMuted; return System.Threading.Tasks.Task.CompletedTask; }, "ms-settings:sound");

        PickerRow.Children.Clear();
        _output = Picker(DataFlow.Render, "");
        _input = Picker(DataFlow.Capture, "");
        Grid.SetColumn(_input, 1);
        _batteryText = new TextBlock
        {
            FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0), Foreground = (Brush)FindResource("TextSecondaryBrush"),
        };
        Grid.SetColumn(_batteryText, 2);
        PickerRow.Children.Add(_output);
        PickerRow.Children.Add(_input);
        PickerRow.Children.Add(_batteryText);
    }

    /// <param name="state">true = on, false = off, null = hardware missing (tile dimmed).</param>
    private void AddTile(string glyph, string label, Func<bool?> state, Func<System.Threading.Tasks.Task> toggle, string? settingsUri)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center };
        var text = new TextBlock { Text = label, FontSize = 9, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var scale = new ScaleTransform(1, 1);
        var tile = new Border
        {
            CornerRadius = new CornerRadius(12), Height = 50, Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(2, 8, 2, 6), Cursor = Cursors.Hand, ToolTip = label,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            Child = new StackPanel { Children = { icon, text } },
            Background = new SolidColorBrush(((SolidColorBrush)FindResource("SurfaceBrush")).Color),
        };

        // Press feedback: scale 0.95, fast ease-out; release is the same speed back.
        void Press(double to)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        tile.PreviewMouseLeftButtonDown += (_, _) => Press(0.95);
        tile.MouseLeave += (_, _) => Press(1);
        tile.MouseLeftButtonUp += async (_, _) =>
        {
            Press(1);
            if (state() == null) return;
            try { await toggle(); } catch { }
            Refresh();
        };
        // Right-click = the full settings page, like long-press in Control Center.
        if (settingsUri != null) tile.MouseRightButtonUp += (_, e) => { e.Handled = true; SystemControlsService.OpenSettings(settingsUri); };

        _tiles.Add((tile, state));
        TileRow.Children.Add(tile);
    }

    private Button Picker(DataFlow flow, string glyph)
    {
        var b = new Button
        {
            Margin = new Thickness(3, 0, 3, 0), Height = 28, Padding = new Thickness(8, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand,
            Background = (Brush)FindResource("SurfaceBrush"), BorderThickness = new Thickness(0),
            Foreground = (Brush)FindResource("TextPrimaryBrush"), FontSize = 10.5,
            Template = PickerTemplate(),
        };
        b.Tag = glyph;
        b.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var d in _controls!.Devices(flow))
            {
                var item = new MenuItem { Header = d.Name, IsCheckable = true, IsChecked = d.IsDefault };
                item.Click += (_, _) => { try { SystemControlsService.SetDefaultDevice(d.Id); } catch { } Refresh(); };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("No devices"), IsEnabled = false });
            menu.IsOpen = true;
        };
        return b;
    }

    private static ControlTemplate PickerTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void Refresh()
    {
        if (_controls == null) return;
        var surface = ((SolidColorBrush)FindResource("SurfaceBrush")).Color;
        var primary = ((SolidColorBrush)FindResource("TextPrimaryBrush")).Color;

        foreach (var (tile, state) in _tiles)
        {
            bool? on = state();
            var bg = on == true ? Accent : surface;
            var fg = on == true ? Colors.White : primary;
            tile.Opacity = on == null ? 0.4 : 1;
            Fade((SolidColorBrush)tile.Background, bg);
            foreach (var t in ((StackPanel)tile.Child).Children.OfType<TextBlock>()) t.Foreground = new SolidColorBrush(fg);
        }

        SetPicker(_output, DataFlow.Render);
        SetPicker(_input, DataFlow.Capture);

        if (_batteryText != null && _battery != null)
        {
            _batteryText.Visibility = _battery.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            _batteryText.Text = $"{(_battery.IsCharging ? "⚡" : "")}{_battery.ChargePercent}%";
        }
    }

    private void SetPicker(Button? b, DataFlow flow)
    {
        if (b == null) return;
        var name = _controls!.Devices(flow).FirstOrDefault(d => d.IsDefault)?.Name ?? "None";
        b.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = (string)b.Tag, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = " ▾", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        b.ToolTip = flow == DataFlow.Render ? $"Output: {name}" : $"Input: {name}";
    }

    // State colour change: 150 ms ease — a colour change, not movement.
    private static void Fade(SolidColorBrush brush, Color to)
    {
        if (brush.Color == to) return;
        if (!SystemParameters.ClientAreaAnimation) { brush.Color = to; return; }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(150)));
    }
}
