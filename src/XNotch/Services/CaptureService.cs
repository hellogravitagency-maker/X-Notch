using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace XNotch.Services;

/// <summary>
/// Screenshots, recordings and text-from-screen, built on Windows' own Snipping Tool:
/// we press its shortcuts and pick the results up from the folders it saves to.
/// New captures land on the shelf (event-driven, no polling).
/// </summary>
public sealed class CaptureService : IDisposable
{
    private FileSystemWatcher? _shots, _videos;
    private SynchronizationContext? _ui;

    /// <summary>Fired on the UI thread with each newly saved screenshot or recording.</summary>
    public event Action<string>? CaptureSaved;

    public static string ScreenshotsFolder
    {
        get
        {
            try
            {
                var id = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f"); // FOLDERID_Screenshots
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) == 0)
                {
                    try { return Marshal.PtrToStringUni(p)!; } finally { Marshal.FreeCoTaskMem(p); }
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
    }

    public static string RecordingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings");

    /// <summary>Starts watching. Call on the UI thread.</summary>
    public void Start() => Start(ScreenshotsFolder, RecordingsFolder);

    public void Start(string screenshotsFolder, string recordingsFolder)
    {
        if (_shots != null) return;
        _ui = SynchronizationContext.Current; // UI thread in the app; null in tests (raise inline)
        _shots = Watch(screenshotsFolder, "*.png");
        _videos = Watch(recordingsFolder, "*.mp4");
    }

    public void Stop()
    {
        _shots?.Dispose(); _videos?.Dispose();
        _shots = _videos = null;
    }

    private FileSystemWatcher? Watch(string folder, string filter)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var w = new FileSystemWatcher(folder, filter) { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
            w.Created += (_, e) => _ = OnCreatedAsync(e.FullPath);
            w.Renamed += (_, e) => _ = OnCreatedAsync(e.FullPath); // recorders often write a temp name then rename
            return w;
        }
        catch { return null; }
    }

    private async Task OnCreatedAsync(string path)
    {
        // The writer may still hold the file (a recording finishes encoding after Stop).
        for (int i = 0; i < 40 && !IsReadable(path); i++) await Task.Delay(250);
        if (!IsReadable(path)) return;
        if (_ui != null) _ui.Post(_ => CaptureSaved?.Invoke(path), null);
        else CaptureSaved?.Invoke(path);
    }

    private static bool IsReadable(string path)
    {
        try { using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
        catch { return false; }
    }

    public static void Snip() => Views.Native.SendWinChord(0x53, shift: true);    // Win+Shift+S
    public static void Record() => Views.Native.SendWinChord(0x52, shift: true);  // Win+Shift+R

    /// <summary>
    /// Opens the snip overlay, waits for the snipped image on the clipboard, reads its text
    /// with Windows OCR and copies that text. Returns the text, or null if cancelled/empty.
    /// </summary>
    public static async Task<string?> TextFromScreenAsync(TimeSpan timeout)
    {
        uint before = GetClipboardSequenceNumber();
        Snip();
        var until = DateTime.Now + timeout;
        BitmapSource? image = null;
        while (DateTime.Now < until)
        {
            await Task.Delay(300);
            if (GetClipboardSequenceNumber() == before) continue;
            try { if (System.Windows.Clipboard.ContainsImage()) { image = System.Windows.Clipboard.GetImage(); break; } }
            catch { } // clipboard busy; try again
        }
        if (image == null) return null;

        var text = await OcrAsync(image);
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { System.Windows.Clipboard.SetText(text); } catch { }
        }
        return text;
    }

    public static async Task<string?> OcrAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) return null;

        // WPF bitmap → PNG bytes → WinRT SoftwareBitmap
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(ms.ToArray().AsBuffer());
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
