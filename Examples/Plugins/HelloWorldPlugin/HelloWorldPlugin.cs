using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XNotch.Models;
using XNotch.Plugins;

namespace HelloWorldPlugin;

[XNotchPlugin("com.xnotch.helloworld", "Hello World", "1.0.0", "X-Notch Team")]
public class HelloWorldPlugin : PluginBase, IUIPlugin
{
    public override string Id => "com.xnotch.helloworld";
    public override string Name => "Hello World";
    public override string Version => "1.0.0";
    public override string Author => "X-Notch Team";
    public override string Description => "A simple test plugin that displays system info in the notch.";
    public override string MinimumXNotchVersion => "0.3.0";

    private TextBlock? _closedLabel;
    private StackPanel? _openPanel;
    private TextBlock? _uptimeText;
    private TextBlock? _timeText;
    private DispatcherTimer? _timer;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateInfo();
        _timer.Start();
    }

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.ClosedContent => GetClosedContent(),
            UIPluginLocation.OpenContent => GetOpenContent(),
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
        if (newState == NotchState.Open)
            UpdateInfo();
    }

    private UIElement GetClosedContent()
    {
        _closedLabel = new TextBlock
        {
            Text = "\U0001F44B",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            ToolTip = "Hello World Plugin"
        };
        return _closedLabel;
    }

    private UIElement GetOpenContent()
    {
        _openPanel = new StackPanel
        {
            Margin = new Thickness(10)
        };

        var title = new TextBlock
        {
            Text = "\U0001F44B Hello World Plugin",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };

        _timeText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        _uptimeText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var machineText = new TextBlock
        {
            Text = $"\U0001F4BB {Environment.MachineName} — {Environment.UserName}",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var osText = new TextBlock
        {
            Text = $"\U0001F5A5\uFE0F {Environment.OSVersion.VersionString}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130))
        };

        _openPanel.Children.Add(title);
        _openPanel.Children.Add(_timeText);
        _openPanel.Children.Add(_uptimeText);
        _openPanel.Children.Add(machineText);
        _openPanel.Children.Add(osText);

        UpdateInfo();
        return _openPanel;
    }

    private void UpdateInfo()
    {
        var now = DateTime.Now;
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        if (_timeText != null)
            _timeText.Text = $"\U0001F552 {now:HH:mm:ss} — {now:dddd d MMMM yyyy}";

        if (_uptimeText != null)
            _uptimeText.Text = $"\u23F1\uFE0F Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
    }

    public override Task ShutdownAsync()
    {
        _timer?.Stop();
        _timer = null;
        return base.ShutdownAsync();
    }

    public override void Dispose()
    {
        _timer?.Stop();
        base.Dispose();
    }
}
