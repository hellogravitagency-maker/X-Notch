using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XNotch.Services;
using XNotch.Views;
using Xunit;

namespace XNotch.Tests;

public class CaptureTests
{
    [Fact]
    public async Task NewScreenshot_InWatchedFolder_IsReported()
    {
        var shots = Directory.CreateTempSubdirectory().FullName;
        var videos = Directory.CreateTempSubdirectory().FullName;
        using var svc = new CaptureService();
        var got = new TaskCompletionSource<string>();
        svc.CaptureSaved += p => got.TrySetResult(p);
        svc.Start(shots, videos);

        var file = Path.Combine(shots, "Screenshot 2026-11-22.png");
        await File.WriteAllBytesAsync(file, [0x89, 0x50, 0x4E, 0x47]);
        var done = await Task.WhenAny(got.Task, Task.Delay(5000));
        Assert.Same(got.Task, done);
        Assert.Equal(file, got.Task.Result);
    }

    [Fact]
    public void Ocr_ReadsRenderedText()
    {
        string? text = null;
        var t = new Thread(() =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 600, 120));
                dc.DrawText(new FormattedText("HELLO XNOTCH 2026", System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 48, Brushes.Black, 1.0), new Point(20, 25));
            }
            var bmp = new RenderTargetBitmap(600, 120, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            text = CaptureService.OcrAsync(bmp).GetAwaiter().GetResult();
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (text == null) return; // no OCR language installed on this machine
        Assert.Contains("HELLO", text.ToUpperInvariant());
    }

    [Fact]
    public void Hex_Format() => Assert.Equal("#0A84FF", ColourPickerWindow.Hex(Color.FromRgb(0x0A, 0x84, 0xFF)));
}
