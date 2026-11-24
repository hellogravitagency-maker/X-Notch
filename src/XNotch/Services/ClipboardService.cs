using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Threading;

namespace XNotch.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClipType { Text, Link, Colour, Path, Code }

public sealed class ClipItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ClipType Type { get; init; }
    public string Text { get; init; } = "";
    public bool Pinned { get; set; }
    public DateTime CopiedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Clipboard history. Event-driven (WM_CLIPBOARDUPDATE), skips content that password
/// managers mark private, and persists DPAPI-encrypted so only this Windows user can read it.
/// </summary>
public sealed partial class ClipboardService : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private readonly List<ClipItem> _items = [];
    private readonly object _lock = new();
    private readonly string _file;
    private HwndSource? _window;
    private string? _ignoreNext; // text we put on the clipboard ourselves

    public int MaxItems { get; set; } = 50;
    public int KeepHours { get; set; } = 24;

    public event Action? Changed;

    public ClipboardService(string? file = null)
    {
        _file = file ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X-Notch", "clipboard.dat");
    }

    public IReadOnlyList<ClipItem> Items
    {
        get { lock (_lock) return _items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.CopiedAt).ToList(); }
    }

    /// <summary>Starts listening. Must run on the UI (STA) thread.</summary>
    public void Start()
    {
        if (_window != null) return;
        Load();
        // Message-only window (parent HWND_MESSAGE): receives clipboard notifications, never shown.
        _window = new HwndSource(new HwndSourceParameters("XNotchClipboard") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(WndProc);
        AddClipboardFormatListener(_window.Handle);
    }

    public void Stop()
    {
        if (_window == null) return;
        RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
        _window = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            // The source app may still hold the clipboard; read a moment later.
            _window?.Dispatcher.BeginInvoke(DispatcherPriority.Background, ReadClipboard);
        return IntPtr.Zero;
    }

    private void ReadClipboard()
    {
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data == null || IsPrivate(data)) return;

            string? text = data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? string.Join(Environment.NewLine, (string[])data.GetData(System.Windows.DataFormats.FileDrop))
                : data.GetDataPresent(System.Windows.DataFormats.UnicodeText) ? data.GetData(System.Windows.DataFormats.UnicodeText) as string : null;
            if (string.IsNullOrWhiteSpace(text)) return;

            if (text == _ignoreNext) { _ignoreNext = null; return; }
            Add(text);
        }
        catch (COMException) { } // clipboard busy; the next change will be caught
        catch (ExternalException) { }
    }

    /// <summary>
    /// Password managers and Windows' own sensitive copies flag their data with these
    /// formats (documented by Microsoft for clipboard history / cloud clipboard).
    /// </summary>
    public static bool IsPrivate(System.Windows.IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") ||
            data.GetDataPresent("Clipboard Viewer Ignore"))
            return true;
        foreach (var fmt in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (data.GetDataPresent(fmt) && data.GetData(fmt) is MemoryStream ms && ms.Length >= 4)
            {
                var b = ms.ToArray();
                if (BitConverter.ToInt32(b, 0) == 0) return true;
            }
        }
        return false;
    }

    public void Add(string text)
    {
        lock (_lock)
        {
            var existing = _items.FirstOrDefault(i => i.Text == text);
            if (existing != null) existing.CopiedAt = DateTime.Now; // re-copy moves it to the top
            else _items.Add(new ClipItem { Text = text, Type = Classify(text) });
            Prune();
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Puts an item back on the clipboard without re-recording it.</summary>
    public void CopyBack(ClipItem item)
    {
        _ignoreNext = item.Text;
        if (item.Type == ClipType.Path && item.Text.Split(Environment.NewLine).All(p => File.Exists(p) || Directory.Exists(p)))
        {
            var files = new System.Collections.Specialized.StringCollection();
            files.AddRange(item.Text.Split(Environment.NewLine));
            System.Windows.Clipboard.SetFileDropList(files);
        }
        else System.Windows.Clipboard.SetText(item.Text);
        lock (_lock) item.CopiedAt = DateTime.Now;
        Save();
        Changed?.Invoke();
    }

    public void TogglePin(ClipItem item) { lock (_lock) item.Pinned = !item.Pinned; Save(); Changed?.Invoke(); }

    public void Remove(ClipItem item) { lock (_lock) _items.Remove(item); Save(); Changed?.Invoke(); }

    /// <summary>Clears everything except pinned items.</summary>
    public void Clear() { lock (_lock) _items.RemoveAll(i => !i.Pinned); Save(); Changed?.Invoke(); }

    private void Prune()
    {
        var cutoff = DateTime.Now.AddHours(-KeepHours);
        _items.RemoveAll(i => !i.Pinned && i.CopiedAt < cutoff);
        foreach (var old in _items.Where(i => !i.Pinned).OrderByDescending(i => i.CopiedAt).Skip(MaxItems).ToList())
            _items.Remove(old);
    }

    [GeneratedRegex(@"^(#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*(,\s*[\d.]+\s*)?\)|hsla?\([^)]*\))$")]
    private static partial Regex ColourRegex();

    [GeneratedRegex(@"[;{}]\s*$|^\s*(using|import|from|def|class|function|const|let|var|public|private|#include|SELECT|fn|package)\b|=>|\)\s*\{", RegexOptions.Multiline)]
    private static partial Regex CodeRegex();

    public static ClipType Classify(string text)
    {
        var t = text.Trim();
        if (ColourRegex().IsMatch(t)) return ClipType.Colour;
        if (!t.Contains('\n') && Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") return ClipType.Link;
        var lines = t.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        if (lines.Length > 0 && lines.All(l => l.Length < 260 && System.IO.Path.IsPathFullyQualified(l) && (File.Exists(l) || Directory.Exists(l))))
            return ClipType.Path;
        if (lines.Length >= 2 && CodeRegex().Matches(t).Count >= 2) return ClipType.Code;
        return ClipType.Text;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(_file), null, DataProtectionScope.CurrentUser));
            var items = JsonSerializer.Deserialize<List<ClipItem>>(json) ?? [];
            lock (_lock) { _items.Clear(); _items.AddRange(items); Prune(); }
        }
        catch { } // unreadable (other user / corrupt): start empty, overwrite on next save
    }

    private void Save()
    {
        try
        {
            string json;
            lock (_lock) json = JsonSerializer.Serialize(_items);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_file)!);
            File.WriteAllBytes(_file, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser));
        }
        catch { }
    }

    /// <summary>Test hook: reload from disk.</summary>
    public void Reload() => Load();

    public void Dispose() => Stop();

    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
