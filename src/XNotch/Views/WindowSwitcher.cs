using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using XNotch.Helpers;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;

namespace XNotch.Views;

/// <summary>
/// Alt-Tab replacement with live DWM thumbnails. Opens instantly with no animation (Alt-Tab is
/// pressed hundreds of times a day). Alt+Tab / Shift: next / previous · arrows · Esc cancels ·
/// releasing Alt switches. Elevated windows still get Windows' own Alt-Tab (UIPI blocks hooks there).
/// </summary>
public sealed class WindowSwitcher : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    private const int VK_TAB = 0x09, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_ESCAPE = 0x1B, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const int LLKHF_ALTDOWN = 0x20, LLKHF_INJECTED = 0x10;

    private HookProc? _proc;
    private IntPtr _hook;
    private SwitcherWindow? _window;

    public bool IsOpen => _window?.IsVisible == true;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _window?.Close();
        _window = null;
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((k.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_hook, code, wParam, lParam); // our own dummy key
        int msg = wParam.ToInt32();
        bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;

        if (down && k.vkCode == VK_TAB && (k.flags & LLKHF_ALTDOWN) != 0)
        {
            bool back = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            if (!IsOpen)
            {
                if (!Open()) return CallNextHookEx(_hook, code, wParam, lParam); // nothing to switch to
                SuppressAltMenu();
                if (back) _window!.Move(-2); // Shift+Alt+Tab starts from the last window
            }
            else _window!.Move(back ? -1 : 1);
            return 1;
        }

        if (IsOpen)
        {
            if (down && k.vkCode == VK_ESCAPE) { _window!.Hide(); return 1; }
            if (down && k.vkCode == VK_LEFT) { _window!.Move(-1); return 1; }
            if (down && k.vkCode == VK_RIGHT) { _window!.Move(1); return 1; }
            if (!down && k.vkCode is VK_MENU or VK_LMENU or VK_RMENU) _window!.Commit(); // let Alt-up through
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool Open()
    {
        var items = RunningApps.WindowsInZOrder();
        if (items.Count < 1) return false;
        _window ??= new SwitcherWindow();
        _window.ShowFor(items);
        return true;
    }

    // Alt pressed and released with no other key reaching the app activates its menu bar.
    // An unassigned virtual key in between prevents that (same trick PowerToys uses).
    private static void SuppressAltMenu()
    {
        keybd_event(0xE8, 0, 0, UIntPtr.Zero);
        keybd_event(0xE8, 0, 2, UIntPtr.Zero);
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)] private struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr extra; }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}

/// <summary>The switcher overlay: a grid of live window thumbnails with icon + title.</summary>
internal sealed class SwitcherWindow : Window
{
    private const double ThumbW = 220, ThumbH = 140;
    private static readonly Color Accent = Color.FromRgb(0x0A, 0x84, 0xFF);

    private readonly WrapPanel _panel = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly List<(IntPtr Hwnd, Border Frame, Border Thumb, IntPtr DwmThumb)> _items = [];
    private int _selected;

    public SwitcherWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            CornerRadius = new CornerRadius(22), Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x1C, 0x1C, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Child = _panel,
        };
        SourceInitialized += (_, _) => WindowHelper.MakeOverlayWindow(this); // never steals focus
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClearThumbnails(); };
    }

    public void ShowFor(List<(IntPtr Hwnd, string Path)> windows)
    {
        ClearThumbnails();
        _panel.Children.Clear();
        _items.Clear();

        var screen = System.Windows.Forms.Screen.FromHandle(GetForegroundWindow());
        double scale = ScreenHelper.GetScaleForScreen(screen);
        int perRow = Math.Max(1, (int)((screen.WorkingArea.Width / scale - 80) / (ThumbW + 16)));
        _panel.MaxWidth = perRow * (ThumbW + 16);

        foreach (var (hwnd, path) in windows.Take(perRow * 3))
        {
            var thumb = new Border { Width = ThumbW, Height = ThumbH, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 2, 0), MaxWidth = ThumbW };
            title.Children.Add(new Image { Source = ShellIcon.Get(path), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
            title.Children.Add(new TextBlock { Text = Native.WindowTitle(hwnd), Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = ThumbW - 24 });
            var frame = new Border
            {
                Padding = new Thickness(6), Margin = new Thickness(2), CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(2.5), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new StackPanel { Children = { thumb, title } },
            };
            int index = _items.Count;
            frame.MouseLeftButtonUp += (_, _) => { _selected = index; Commit(); };
            _panel.Children.Add(frame);
            _items.Add((hwnd, frame, thumb, IntPtr.Zero));
        }

        _selected = _items.Count > 1 ? 1 : 0; // index 0 is the window you're in
        Show();
        UpdateLayout();
        Left = screen.WorkingArea.Left / scale + (screen.WorkingArea.Width / scale - ActualWidth) / 2;
        Top = screen.WorkingArea.Top / scale + (screen.WorkingArea.Height / scale - ActualHeight) / 2;
        UpdateLayout();
        RegisterThumbnails(scale);
        Highlight();
    }

    public void Move(int delta)
    {
        if (_items.Count == 0) return;
        _selected = ((_selected + delta) % _items.Count + _items.Count) % _items.Count;
        Highlight();
    }

    /// <summary>Instant selection ring; no motion.</summary>
    private void Highlight()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            _items[i].Frame.BorderBrush = i == _selected ? new SolidColorBrush(Accent) : Brushes.Transparent;
            _items[i].Frame.Background = i == _selected ? new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
        }
    }

    public void Commit()
    {
        if (!IsVisible) return;
        var target = _selected < _items.Count ? _items[_selected].Hwnd : IntPtr.Zero;
        Hide();
        if (target == IntPtr.Zero) return;
        if (Native.IsIconic(target)) Native.ShowWindow(target, 9 /* SW_RESTORE */);
        SwitchToThisWindow(target, true); // also works while another app holds the foreground lock
    }

    private void RegisterThumbnails(double scale)
    {
        var me = new WindowInteropHelper(this).Handle;
        for (int i = 0; i < _items.Count; i++)
        {
            var (hwnd, frame, thumb, _) = _items[i];
            if (DwmRegisterThumbnail(me, hwnd, out var id) != 0) continue;
            DwmQueryThumbnailSourceSize(id, out var src);
            var topLeft = thumb.TranslatePoint(new Point(0, 0), this);
            // Fit the window's aspect inside the slot, in physical pixels relative to our window.
            double sw = Math.Max(1, src.cx), sh = Math.Max(1, src.cy);
            double fit = Math.Min(ThumbW / sw, ThumbH / sh);
            double w = sw * fit, h = sh * fit;
            double x = topLeft.X + (ThumbW - w) / 2, y = topLeft.Y + (ThumbH - h) / 2;
            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = 0x1 | 0x8 | 0x10, // RECTDESTINATION | VISIBLE | SOURCECLIENTAREAONLY
                rcDestination = new RECT { left = (int)(x * scale), top = (int)(y * scale), right = (int)((x + w) * scale), bottom = (int)((y + h) * scale) },
                fVisible = true, fSourceClientAreaOnly = false,
            };
            DwmUpdateThumbnailProperties(id, ref props);
            thumb.Background = Brushes.Transparent;
            _items[i] = (hwnd, frame, thumb, id);
        }
    }

    private void ClearThumbnails()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].DwmThumb != IntPtr.Zero) DwmUnregisterThumbnail(_items[i].DwmThumb);
            _items[i] = (_items[i].Hwnd, _items[i].Frame, _items[i].Thumb, IntPtr.Zero);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public RECT rcDestination, rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
