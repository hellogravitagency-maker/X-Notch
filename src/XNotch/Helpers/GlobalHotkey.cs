using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace XNotch.Helpers;

/// <summary>
/// Registers global hotkeys:
///   - Ctrl+Alt+N to toggle the notch overlay
///   - Ctrl+Space to summon the Spotlight Launcher HUD
/// </summary>
public class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_TOGGLE_ID = 9000;
    private const int HOTKEY_SPOTLIGHT_ID = 9001;

    // Modifiers
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;

    // Virtual keys
    private const uint VK_SPACE = 0x20;
    private const uint VK_N = 0x4E;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _source;

    public event Action? HotkeyPressed;
    public event Action? SpotlightHotkeyPressed;

    public void Register(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Ctrl+Alt+N to toggle the notch
        RegisterHotKey(_hwnd, HOTKEY_TOGGLE_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_N);

        // Ctrl+Space to summon Spotlight Launcher HUD
        RegisterHotKey(_hwnd, HOTKEY_SPOTLIGHT_ID, MOD_CONTROL | MOD_NOREPEAT, VK_SPACE);
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_TOGGLE_ID);
            UnregisterHotKey(_hwnd, HOTKEY_SPOTLIGHT_ID);
            _source?.RemoveHook(WndProc);
            _source = null;
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_TOGGLE_ID)
            {
                HotkeyPressed?.Invoke();
                handled = true;
            }
            else if (id == HOTKEY_SPOTLIGHT_ID)
            {
                SpotlightHotkeyPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}
