using System;
using System.IO;
using System.Linq;
using System.Text;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class ClipboardTests
{
    [Theory]
    [InlineData("#0A84FF", ClipType.Colour)]
    [InlineData("rgb(10, 132, 255)", ClipType.Colour)]
    [InlineData("https://example.com/a?b=1", ClipType.Link)]
    [InlineData("hello world", ClipType.Text)]
    [InlineData("public void A()\n{\n    return;\n}", ClipType.Code)]
    [InlineData("Line one.\nLine two.", ClipType.Text)]
    public void Classify(string text, ClipType expected) => Assert.Equal(expected, ClipboardService.Classify(text));

    [Fact]
    public void Classify_ExistingPath()
    {
        var f = Path.GetTempFileName();
        Assert.Equal(ClipType.Path, ClipboardService.Classify(f));
        File.Delete(f);
    }

    [Fact]
    public void History_Dedupes_Caps_KeepsPinned_AndPersistsEncrypted()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat");
        var svc = new ClipboardService(file) { MaxItems = 3 };
        svc.Add("secret-ish text");
        svc.Add("b");
        svc.Add("secret-ish text"); // re-copy moves to top, no duplicate
        Assert.Equal(2, svc.Items.Count);
        Assert.Equal("secret-ish text", svc.Items[0].Text);

        svc.TogglePin(svc.Items.Single(i => i.Text == "b"));
        svc.Add("c"); svc.Add("d"); svc.Add("e"); // cap = 3 unpinned
        Assert.Contains(svc.Items, i => i.Text == "b" && i.Pinned);
        Assert.Equal(3, svc.Items.Count(i => !i.Pinned));
        Assert.Equal("b", svc.Items[0].Text); // pinned first

        Assert.DoesNotContain("secret-ish", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        var reloaded = new ClipboardService(file);
        reloaded.Reload();
        Assert.Equal(svc.Items.Select(i => i.Text), reloaded.Items.Select(i => i.Text));

        reloaded.Clear();
        Assert.Equal("b", reloaded.Items.Single().Text);
        File.Delete(file);
    }

    [Fact]
    public void PasswordManagerCopies_AreFlaggedPrivate()
    {
        var plain = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "x");
        Assert.False(ClipboardService.IsPrivate(plain));

        var excluded = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "pw");
        excluded.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[1]));
        Assert.True(ClipboardService.IsPrivate(excluded));

        var noHistory = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "pw");
        noHistory.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        Assert.True(ClipboardService.IsPrivate(noHistory));
    }
}
