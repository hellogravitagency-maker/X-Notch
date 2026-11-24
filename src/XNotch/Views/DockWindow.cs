using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using XNotch.Helpers;
using XNotch.Models;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using FontFamily = System.Windows.Media.FontFamily;

namespace XNotch.Views;

/// <summary>
/// macOS dock that replaces the Windows taskbar while enabled: a floating frosted-glass shelf
/// that hides off the bottom edge and slides up when the pointer touches it. Pinned + running
/// apps, Trash, running dots, cursor magnification, launch bounce, genie target, status area.
/// </summary>
public sealed class DockWindow : Window
{
    private const double Gap = 6;
    private const double Float = 8;        // DIP gap between the shelf and the screen edge (macOS floats it)
    private const double PadY = 6;         // shelf padding above/below the icons
    private const int CapturePad = 24;     // px of extra backdrop so the blur never shows a soft edge

    private double IconSize => Math.Clamp(Settings.DockIconSize, 32, 80);
    private double MaxScale => Settings.DockMagnification ? Math.Clamp(Settings.DockMagnifiedScale, 1.1, 2.2) : 1;
    private double Reach => IconSize * 2.8; // DIP from the cursor where magnification fades out
    private double ShelfHeight => IconSize + PadY * 2 + 6;

    public AppSettings Settings { get; set; }
    public System.Windows.Forms.Screen Screen { get; private set; }

    private readonly Grid _root = new() { Background = Brushes.Transparent };
    private readonly Grid _slideHost = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TranslateTransform _slide = new();
    private readonly Border _shadow = new();
    private readonly Grid _shelf = new() { VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Border _tint = new() { BorderThickness = new Thickness(1) };
    private readonly System.Windows.Controls.Image _backdrop = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly RectangleGeometry _clip = new();
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _clock = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
    private readonly TextBlock _battery = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _glassTimer = new() { Interval = TimeSpan.FromMilliseconds(50) }; // 20 fps under heavy blur
    private readonly List<Slot> _slots = [];
    private readonly AppBarHelper _appBar = new();
    private WriteableBitmap? _wb;
    private double _mouseX = double.NaN;
    private bool _animating, _shown = true;
    private string _lastSignature = "";

    private sealed class Slot
    {
        public required Grid Host;
        public required ScaleTransform Scale;
        public required TranslateTransform Lift;
        public double Current = 1, Target = 1;
        public string? Path;
    }

    public DockWindow(AppSettings settings, System.Windows.Forms.Screen screen)
    {
        Settings = settings;
        Screen = screen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Title = "X-Notch Dock";
        foreach (var t in new[] { _clock, _status, _battery }) t.SetResourceReference(TextBlock.ForegroundProperty, "DockTextBrush");

        // Shelf = soft shadow (own element, so magnification never re-renders the blur) + frosted glass + hairline rim.
        _shadow.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.32, Color = Colors.Black };
        _shadow.SetResourceReference(Border.BackgroundProperty, "DockTintBrush");
        _tint.SetResourceReference(Border.BackgroundProperty, "DockTintBrush");
        _tint.SetResourceReference(Border.BorderBrushProperty, "DockRimBrush");
        _backdrop.Effect = new BlurEffect { Radius = 30, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        _shelf.Clip = _clip;
        _shelf.Children.Add(_backdrop);
        _shelf.Children.Add(_tint);

        var content = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 12, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(_row);

        // Tray chevron: brings Windows' own taskbar back for a moment, so every tray icon
        // stays the real, working one (no fake re-implementation of the notification area).
        var tray = StatusButton(Glyph("", 12), Loc.T("Tray icons"), () =>
        {
            TaskbarHelper.Peek(TimeSpan.FromSeconds(8));
            Visibility = Visibility.Hidden; // let the taskbar have the bottom edge
        });
        var quick = StatusButton(new StackPanel { Orientation = Orientation.Horizontal, Children = { _status, _battery } }, Loc.T("Quick Settings"), () => Native.SendWinChord(0x41)); // Win+A
        var clock = StatusButton(_clock, Loc.T("Notifications & calendar"), () => Native.SendWinChord(0x4E));                                              // Win+N
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Children = { Divider(), tray, quick, clock } };
        Grid.SetColumn(right, 1);
        content.Children.Add(right);

        // Shelf and icons are siblings: magnified icons rise above the shelf without being clipped.
        _slideHost.RenderTransform = _slide;
        _slideHost.Children.Add(_shadow);
        _slideHost.Children.Add(_shelf);
        _slideHost.Children.Add(content);
        _root.Children.Add(_slideHost);
        // Edge sensor: a 2 px, nearly invisible strip still takes the pointer when the dock is hidden.
        _root.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) });
        Content = _root;

        _shelf.SizeChanged += (_, _) => Reshape();
        MouseMove += (_, e) => { _mouseX = e.GetPosition(_row).X; Retarget(); };
        MouseEnter += (_, _) => { _hideTimer.Stop(); Reveal(); };
        MouseLeave += (_, _) => { _mouseX = double.NaN; Retarget(); if (Settings.DockAutoHide) _hideTimer.Start(); };
        _hideTimer.Tick += (_, _) =>
        {
            if (IsMouseOver || _row.Children.OfType<FrameworkElement>().Any(c => c.ContextMenu?.IsOpen == true)) return;
            _hideTimer.Stop();
            Conceal();
        };
        _glassTimer.Tick += (_, _) => CaptureBackdrop();

        _tick.Tick += (_, _) => OnTick();
        SourceInitialized += (_, _) =>
        {
            WindowHelper.MakeOverlayWindow(this);
            WindowHelper.DisableActivation(this);
            WindowHelper.SetExcludeFromCapture(this, true); // the backdrop capture must not see the dock itself
            _appBar.Attach(this);
            Place();
            TaskbarHelper.Hide();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        };
        Loaded += (_, _) =>
        {
            OnTick();
            _tick.Start();
            _glassTimer.Start();
            if (Settings.DockAutoHide) { _hideTimer.Start(); }
        };
        Closed += (_, _) =>
        {
            _tick.Stop();
            _glassTimer.Stop();
            _hideTimer.Stop();
            _appBar.Dispose(); // the App restores the taskbar once the last dock closes
        };
    }

    // Explorer restarts re-create the taskbar: hide it again.
    private static readonly int TaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == TaskbarCreated) Dispatcher.BeginInvoke(() => { TaskbarHelper.Hide(force: true); Place(); });
        return IntPtr.Zero;
    }

    public void MoveTo(System.Windows.Forms.Screen screen) { Screen = screen; Place(); }

    /// <summary>Settings changed: resize, re-reserve, rebuild icons.</summary>
    public void ApplySettings(AppSettings settings)
    {
        Settings = settings;
        _lastSignature = "";
        Place();
        OnTick();
        if (settings.DockAutoHide) _hideTimer.Start(); else { _hideTimer.Stop(); Reveal(); }
    }

    public void Place()
    {
        var screen = Screen;
        double scale = ScreenHelper.GetScaleForScreen(screen);
        int height = (int)Math.Round((IconSize * MaxScale + PadY * 2 + Float + 28) * scale);
        var b = screen.Bounds;
        // Auto-hide floats over windows (macOS default); always-on reserves its strip like the taskbar.
        if (Settings.DockAutoHide) _appBar.Release();
        else _appBar.ReserveBottom(b, (int)Math.Round((ShelfHeight + Float) * scale));
        WindowHelper.SetWindowRectPhysical(this, b.Left, b.Bottom - height, b.Width, height);
        _shelf.Height = ShelfHeight;
        _shadow.Height = ShelfHeight;
        _shadow.VerticalAlignment = VerticalAlignment.Bottom;
        _slideHost.Margin = new Thickness(0, 0, 0, Float);
    }

    /// <summary>Centre of the app's icon in physical px (genie target), revealing the dock if it is hidden.</summary>
    public System.Drawing.Point? IconPoint(string? key)
    {
        if (!IsVisible) return null;
        var slot = _slots.FirstOrDefault(s => key != null && string.Equals(s.Path, key, StringComparison.OrdinalIgnoreCase));
        if (slot == null) return null;
        Reveal();
        if (Settings.DockAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); }
        var c = slot.Host.TranslatePoint(new Point(slot.Host.ActualWidth / 2, slot.Host.ActualHeight - IconSize / 2 - PadY), _root);
        c.Y -= _slide.Y; // land where the icon rests, not where the slide animation is right now
        var p = _root.PointToScreen(c);
        return new System.Drawing.Point((int)p.X, (int)p.Y);
    }

    public int IconPixels => (int)(IconSize * ScreenHelper.GetScaleForScreen(Screen));

    private void Reveal() => Slide(true);
    private void Conceal() => Slide(false);

    // Enter 240 ms strong ease-out; exit faster (180 ms). Reduced motion: no travel, just a fade.
    private void Slide(bool show)
    {
        if (show == _shown) return;
        _shown = show;
        double to = show ? 0 : ShelfHeight + Float + 4;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(show ? 240 : 180);
        if (!SystemParameters.ClientAreaAnimation)
        {
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.Y = 0;
            _slideHost.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(150)));
            return;
        }
        _slideHost.Opacity = 1;
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, d) { EasingFunction = ease });
    }

    private void Reshape()
    {
        double r = Math.Min(22, ShelfHeight * 0.32);
        var rect = new Rect(0, 0, _shelf.ActualWidth, _shelf.ActualHeight);
        _clip.Rect = rect;
        _clip.RadiusX = _clip.RadiusY = r;
        _tint.CornerRadius = _shadow.CornerRadius = new CornerRadius(r);
        _shadow.Width = _shelf.ActualWidth;
    }

    /// <summary>Frosted glass: blurred, slightly saturated copy of whatever is behind the shelf.</summary>
    private void CaptureBackdrop()
    {
        if (!IsVisible || _shelf.ActualWidth < 10 || (!_shown && _slide.Y > ShelfHeight)) return;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var tl = _shelf.PointToScreen(new Point(0, 0));
            int w = (int)(_shelf.ActualWidth * dpi.DpiScaleX) + CapturePad * 2;
            int h = (int)(_shelf.ActualHeight * dpi.DpiScaleY) + CapturePad * 2;
            if (_wb == null || _wb.PixelWidth != w || _wb.PixelHeight != h)
            {
                _wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                _backdrop.Source = _wb;
            }
            BackdropBlurHelper.CaptureInto(_wb, (int)tl.X - CapturePad, (int)tl.Y - CapturePad, w, h, padding: 0, refract: false);
            _backdrop.Margin = new Thickness(-CapturePad / dpi.DpiScaleX, -CapturePad / dpi.DpiScaleY, -CapturePad / dpi.DpiScaleX, -CapturePad / dpi.DpiScaleY);
        }
        catch { }
    }

    // ponytail: 1 s poll for running windows + taskbar re-show. Upgrade path:
    // SetWinEventHook(EVENT_OBJECT_SHOW/DESTROY) if this ever shows up in a profile.
    private void OnTick()
    {
        _clock.Text = DateTime.Now.ToString("t");
        UpdateStatus();
        if (TaskbarHelper.IsPeeking) return;
        if (Visibility != Visibility.Visible) Visibility = Visibility.Visible; // peek ended
        TaskbarHelper.Hide();

        var running = Settings.DockAppsOnOwnScreen ? RunningApps.Enumerate(Screen) : RunningApps.Enumerate();
        var pins = Settings.DockPins.Where(AppIdentity.Exists).ToList();
        var extra = running.Keys.Where(p => !pins.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

        string signature = IconSize + "|" + MaxScale + "#" + string.Join("|", pins) + "#" + string.Join("|", extra) + "#" + string.Join("|", running.Keys);
        if (signature == _lastSignature) return;
        _lastSignature = signature;
        Rebuild(pins, extra, running);
    }

    private const string TrashKey = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    private void Rebuild(List<string> pins, List<string> extra, Dictionary<string, List<IntPtr>> running)
    {
        _row.Children.Clear();
        _slots.Clear();

        _row.Children.Add(MakeSlot(null, StartGlyph(), "Start", false, () => Native.SendWinChord(null)));

        foreach (var p in pins) AddApp(p, running);
        if (extra.Count > 0)
        {
            _row.Children.Add(Divider());
            foreach (var p in extra) AddApp(p, running);
        }

        // Trash sits after a divider, like macOS.
        _row.Children.Add(Divider());
        var trash = new System.Windows.Controls.Image { Source = ShellIcon.Get(TrashKey), Width = IconSize, Height = IconSize };
        RenderOptions.SetBitmapScalingMode(trash, BitmapScalingMode.HighQuality);
        _row.Children.Add(MakeSlot(TrashKey, trash, Loc.T("Recycle Bin"), false,
            () => Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true })));
        Retarget();
    }

    private FrameworkElement Divider()
    {
        var r = new Rectangle { Width = 1, Height = IconSize * 0.72, Margin = new Thickness(Gap, 0, Gap, PadY + IconSize * 0.14), VerticalAlignment = VerticalAlignment.Bottom };
        r.SetResourceReference(Shape.FillProperty, "DockRimBrush");
        return r;
    }

    private TextBlock Glyph(string g, double size)
    {
        var t = new TextBlock { Text = g, FontSize = size, FontFamily = _status.FontFamily, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "DockTextBrush");
        return t;
    }

    private void AddApp(string path, Dictionary<string, List<IntPtr>> running)
    {
        bool isRunning = running.TryGetValue(path, out var windows);
        var icon = new System.Windows.Controls.Image { Source = ShellIcon.Get(path), Width = IconSize, Height = IconSize };
        // Store apps don't show up as files; their tile icon comes from the AppsFolder item.
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        FrameworkElement? slotRef = null;
        slotRef = MakeSlot(path, icon, AppIdentity.DisplayName(path), isRunning, () =>
        {
            var wins = RunningApps.Enumerate().GetValueOrDefault(path);
            if (wins is { Count: > 0 })
            {
                // Minimized window + genie on: pour it back out of this icon.
                if (Native.IsIconic(wins[0]) && GenieService.Instance?.Restore(wins[0]) == true) return;
                Native.ToggleWindow(wins[0]);
            }
            else Launch(path, slotRef!);
        });

        var menu = new ContextMenu();
        bool pinned = Settings.DockPins.Contains(path, StringComparer.OrdinalIgnoreCase);
        var pin = new MenuItem { Header = pinned ? Loc.T("Remove from Dock") : Loc.T("Keep in Dock") };
        pin.Click += (_, _) =>
        {
            if (pinned) Settings.DockPins.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
            else Settings.DockPins.Add(path);
            Settings.Save();
            _lastSignature = "";
            OnTick();
        };
        menu.Items.Add(pin);
        if (isRunning)
        {
            var quit = new MenuItem { Header = Loc.T("Quit") };
            quit.Click += (_, _) => windows!.ForEach(Native.Close);
            menu.Items.Add(quit);
        }
        slotRef.ContextMenu = menu;
        _row.Children.Add(slotRef);
    }

    // macOS name label: dark rounded bubble above the icon.
    private static readonly Style LabelStyle = (Style)System.Windows.Markup.XamlReader.Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ToolTip">
          <Setter Property="OverridesDefaultStyle" Value="True"/>
          <Setter Property="HasDropShadow" Value="False"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ToolTip">
                <Border Background="#E6262628" BorderBrush="#33FFFFFF" BorderThickness="1" CornerRadius="7" Padding="10,4">
                  <ContentPresenter TextElement.Foreground="White" TextElement.FontSize="12.5"/>
                </Border>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    private FrameworkElement MakeSlot(string? path, UIElement visual, string name, bool running, Action click)
    {
        var scale = new ScaleTransform(1, 1);
        var lift = new TranslateTransform();
        var face = new Grid
        {
            Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0.5, 1),
            RenderTransform = new TransformGroup { Children = { scale, lift } },
            Margin = new Thickness(0, 0, 0, PadY + 3),
            Children = { visual },
        };
        var host = new Grid
        {
            Width = IconSize + Gap, Height = IconSize * MaxScale + PadY * 2 + 20, Background = Brushes.Transparent,
            Cursor = Cursors.Hand, ToolTip = new System.Windows.Controls.ToolTip { Content = name, Style = LabelStyle },
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ToolTipService.SetPlacement(host, System.Windows.Controls.Primitives.PlacementMode.Top);
        ToolTipService.SetVerticalOffset(host, IconSize * (MaxScale - 1) - 4);
        ToolTipService.SetInitialShowDelay(host, 250);
        ToolTipService.SetBetweenShowDelay(host, 0); // once one label shows, neighbours show instantly
        host.Children.Add(face);
        if (running)
        {
            var dot = new Ellipse { Width = 4, Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1.5), Opacity = 0.85 };
            dot.SetResourceReference(Shape.FillProperty, "DockTextBrush");
            host.Children.Add(dot);
        }

        // Press feedback: instant dim, no motion (used dozens of times a day).
        host.PreviewMouseLeftButtonDown += (_, _) => face.Opacity = 0.6;
        host.MouseLeave += (_, _) => face.Opacity = 1;
        host.MouseLeftButtonUp += (_, _) => { face.Opacity = 1; click(); };

        _slots.Add(new Slot { Host = host, Scale = scale, Lift = lift, Path = path });
        return host;
    }

    // Magnification: target scale from cursor distance (cosine falloff), then a
    // per-frame critically-damped approach so it tracks smoothly and stays interruptible.
    private void Retarget()
    {
        foreach (var s in _slots)
        {
            if (double.IsNaN(_mouseX) || MaxScale <= 1 || !SystemParameters.ClientAreaAnimation) { s.Target = 1; continue; }
            double center = s.Host.TranslatePoint(new Point(s.Host.ActualWidth / 2, 0), _row).X;
            double d = Math.Abs(_mouseX - center);
            s.Target = d >= Reach ? 1 : 1 + (MaxScale - 1) * (Math.Cos(Math.PI * d / Reach) + 1) / 2;
        }
        if (!_animating) { _animating = true; CompositionTarget.Rendering += OnFrame; }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        bool settled = true;
        foreach (var s in _slots)
        {
            s.Current += (s.Target - s.Current) * 0.28;
            if (Math.Abs(s.Target - s.Current) < 0.002) s.Current = s.Target; else settled = false;
            s.Scale.ScaleX = s.Scale.ScaleY = s.Current;
            s.Host.Width = IconSize * s.Current + Gap; // neighbours make room, like macOS
        }
        if (settled) { CompositionTarget.Rendering -= OnFrame; _animating = false; }
    }

    private static void Launch(string path, FrameworkElement slotHost)
    {
        if (!AppIdentity.Launch(path)) return;

        // Launch bounce: rare and confirms the click. Ease-out up, ease-in down, twice.
        if (!SystemParameters.ClientAreaAnimation || slotHost is not Grid g || g.Children[0] is not Grid face) return;
        if (face.RenderTransform is not TransformGroup tg || tg.Children[1] is not TranslateTransform lift) return;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        for (int i = 0; i < 2; i++)
        {
            double t = i * 450;
            a.KeyFrames.Add(new EasingDoubleKeyFrame(-16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t + 200)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t + 450)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        }
        lift.BeginAnimation(TranslateTransform.YProperty, a);
    }

    private Border StatusButton(UIElement content, string tip, Action click)
    {
        var b = new Border
        {
            Child = content, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = new System.Windows.Controls.ToolTip { Content = tip, Style = LabelStyle },
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, PadY + IconSize / 2 - 13),
        };
        ToolTipService.SetPlacement(b, System.Windows.Controls.Primitives.PlacementMode.Top);
        // Hover tint only on real pointers; press = instant dim (used many times a day, no motion).
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x24, 0x80, 0x80, 0x80));
        b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; b.Opacity = 1; };
        b.PreviewMouseLeftButtonDown += (_, _) => b.Opacity = 0.7;
        b.MouseLeftButtonUp += (_, _) => { b.Opacity = 1; click(); };
        return b;
    }

    /// <summary>Wi-Fi / Ethernet + volume glyphs and battery, like the taskbar's quick-settings button.</summary>
    private void UpdateStatus()
    {
        var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                     && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                     && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel).ToList();
        string net = nics.Any(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211) ? ""
            : nics.Count > 0 ? "" : "";
        _status.Text = net + "  ";
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        bool hasBattery = power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
        _battery.Text = hasBattery ? $"  {(power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "⚡" : "")}{power.BatteryLifePercent * 100:0}%" : "";
    }

    private UIElement StartGlyph()
    {
        var g = new UniformGrid4(IconSize * 0.4);
        return new Border
        {
            Width = IconSize, Height = IconSize, CornerRadius = new CornerRadius(IconSize * 0.23), // macOS squircle-ish radius
            Background = new LinearGradientBrush(Color.FromRgb(0x4D, 0xA6, 0xFF), Color.FromRgb(0x00, 0x5F, 0xD8), 90),
            Child = g,
        };
    }

    /// <summary>The four Windows squares.</summary>
    private sealed class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid4(double size)
        {
            Rows = 2; Columns = 2; Width = size; Height = size;
            HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < 4; i++) Children.Add(new Rectangle { Fill = Brushes.White, Margin = new Thickness(1), RadiusX = 1, RadiusY = 1 });
        }
    }
}

/// <summary>
/// Hides/restores the Windows taskbar. Crash-safe: a marker file remembers the original
/// auto-hide state so the next launch can restore it if the process died.
/// </summary>
public static class TaskbarHelper
{
    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
    private static readonly string Marker = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X-Notch", "taskbar-hidden");

    public static bool IsHiddenByUs => File.Exists(Marker);

    private static DateTime _peekUntil;

    /// <summary>True while the user is using the real taskbar (tray icons). Extends while the pointer is on it.</summary>
    public static bool IsPeeking
    {
        get
        {
            if (DateTime.Now >= _peekUntil) return false;
            var tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero && Native.GetWindowRect(tray, out var r) && System.Windows.Forms.Cursor.Position is var c
                && c.X >= r.left && c.X < r.right && c.Y >= r.top && c.Y < r.bottom)
                _peekUntil = DateTime.Now.AddSeconds(3);
            return true;
        }
    }

    /// <summary>Shows Windows' taskbar (tray icons, quick settings) for a short while.</summary>
    public static void Peek(TimeSpan duration)
    {
        _peekUntil = DateTime.Now + duration;
        foreach (var w in Trays()) Native.ShowWindow(w, 5 /* SW_SHOW */);
    }

    public static void Hide(bool force = false)
    {
        if (!force && DateTime.Now < _peekUntil) return;
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;

        if (!IsHiddenByUs)
        {
            int state = (int)Native.AppBar(ABM_GETSTATE, tray, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(Marker)!);
            File.WriteAllText(Marker, state.ToString());
            Native.AppBar(ABM_SETSTATE, tray, ABS_AUTOHIDE | ABS_ALWAYSONTOP); // frees the work area
            force = true;
        }

        foreach (var w in Trays())
            if (force || Native.IsWindowVisible(w)) Native.ShowWindow(w, 0 /* SW_HIDE */);
    }

    public static void Restore()
    {
        if (!IsHiddenByUs) return;
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        int state = int.TryParse(File.ReadAllText(Marker), out var s) ? s : ABS_ALWAYSONTOP;
        if (tray != IntPtr.Zero) Native.AppBar(ABM_SETSTATE, tray, state);
        foreach (var w in Trays()) Native.ShowWindow(w, 5 /* SW_SHOW */);
        File.Delete(Marker);
    }

    private static IEnumerable<IntPtr> Trays()
    {
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        IntPtr w = IntPtr.Zero;
        while ((w = Native.FindWindowEx(IntPtr.Zero, w, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return w;
    }
}

/// <summary>Top-level app windows, grouped by executable path (what a taskbar shows).</summary>
public static class RunningApps
{
    public static Dictionary<string, List<IntPtr>> Enumerate()
    {
        var result = new Dictionary<string, List<IntPtr>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (h, path) in WindowsInZOrder())
        {
            if (!result.TryGetValue(path, out var list)) result[path] = list = [];
            list.Add(h);
        }
        return result;
    }

    /// <summary>Running apps whose windows are on the given screen ("apps on their own screen").</summary>
    public static Dictionary<string, List<IntPtr>> Enumerate(System.Windows.Forms.Screen screen)
    {
        var result = new Dictionary<string, List<IntPtr>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (h, key) in WindowsInZOrder())
        {
            if (System.Windows.Forms.Screen.FromHandle(h).DeviceName != screen.DeviceName) continue;
            if (!result.TryGetValue(key, out var list)) result[key] = list = [];
            list.Add(h);
        }
        return result;
    }

    /// <summary>App windows, front-most first (EnumWindows walks the z-order top-down).</summary>
    public static List<(IntPtr Hwnd, string Path)> WindowsInZOrder()
    {
        var result = new List<(IntPtr, string)>();
        int self = Environment.ProcessId;
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            long ex = Native.GetWindowLongPtr(h, -20 /* GWL_EXSTYLE */).ToInt64();
            if ((ex & 0x80 /* WS_EX_TOOLWINDOW */) != 0 || Native.GetWindowTextLength(h) == 0 || Native.IsCloaked(h)) return true;

            Native.GetWindowThreadProcessId(h, out int pid);
            if (pid == self && Native.WindowTitle(h) != SettingsWindow.WindowTitle) return true; // own Settings window shows in the dock
            var path = Native.ProcessPath(pid);
            if (path == null || path.EndsWith(@"\explorer.exe",StringComparison.OrdinalIgnoreCase) && Native.ClassName(h) == "Progman") return true;
            result.Add((h, AppIdentity.Key(h, path))); // Store apps → shell:AppsFolder\AUMID
            return true;
        }, IntPtr.Zero);
        return result;
    }
}

/// <summary>Crisp 96–256 px shell icons via IShellItemImageFactory (same source Explorer uses).</summary>
public static class ShellIcon
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) == 0)
            {
                if (factory.GetImage(new SIZE { cx = 96, cy = 96 }, 0x1 /* SIIGBF_BIGGERSIZEOK */, out var hbmp) == 0)
                {
                    img = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    img.Freeze();
                    DeleteObject(hbmp);
                }
                Marshal.ReleaseComObject(factory);
            }
        }
        catch { }
        return Cache[path] = img;
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
}

internal static class Native
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(uint msg, ref APPBARDATA data);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint cb; public uint edge; public int l, t, r, b; public IntPtr lParam; }

    public static uint AppBar(uint msg, IntPtr tray, int state)
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray, lParam = state };
        return SHAppBarMessage(msg, ref d);
    }

    public static bool IsCloaked(IntPtr h) => DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int c, 4) == 0 && c != 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);

    public static string WindowTitle(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static readonly Dictionary<int, string?> PathCache = [];

    public static string? ProcessPath(int pid)
    {
        if (PathCache.TryGetValue(pid, out var p)) return p;
        var h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return PathCache[pid] = QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Taskbar click semantics: front window minimizes, otherwise restore + activate.</summary>
    public static void ToggleWindow(IntPtr h)
    {
        if (GetForegroundWindow() == h && !IsIconic(h)) { ShowWindow(h, 6 /* SW_MINIMIZE */); return; }
        if (IsIconic(h)) ShowWindow(h, 9 /* SW_RESTORE */);
        SetForegroundWindow(h);
    }

    public static void Close(IntPtr h) => PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Presses Win (alone opens Start) or Win+key.</summary>
    public static void SendWinChord(byte? key, bool shift = false)
    {
        const byte LWIN = 0x5B, SHIFT = 0x10; const uint UP = 0x2;
        keybd_event(LWIN, 0, 0, UIntPtr.Zero);
        if (shift) keybd_event(SHIFT, 0, 0, UIntPtr.Zero);
        if (key is byte k) { keybd_event(k, 0, 0, UIntPtr.Zero); keybd_event(k, 0, UP, UIntPtr.Zero); }
        if (shift) keybd_event(SHIFT, 0, UP, UIntPtr.Zero);
        keybd_event(LWIN, 0, UP, UIntPtr.Zero);
    }
}
