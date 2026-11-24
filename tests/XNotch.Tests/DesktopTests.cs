using System;
using System.Linq;
using XNotch.Views;
using Xunit;

namespace XNotch.Tests;

public class DesktopTests
{
    [Fact]
    public void WallpaperLayer_IsFoundBehindIcons()
    {
        // Works on both layouts: Win11 24H2 (WorkerW under Progman) and older (top-level WorkerW).
        Assert.NotEqual(IntPtr.Zero, LiveWallpaperWindow.FindWallpaperLayer());
    }

    [Fact]
    public void WindowList_IsZOrdered_ExcludesSelf_AndMatchesDockGrouping()
    {
        var ordered = RunningApps.WindowsInZOrder();
        Assert.Equal(ordered.Count, ordered.Select(w => w.Hwnd).Distinct().Count());
        var grouped = RunningApps.Enumerate();
        Assert.Equal(ordered.Count, grouped.Values.Sum(l => l.Count));
        Assert.All(ordered, w => Assert.False(string.IsNullOrEmpty(w.Path)));
    }
}

public class AppIdentityTests
{
    private const string SettingsApp = @"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    [Fact]
    public void StoreApp_ResolvesNameAndIcon_WithoutLaunching()
    {
        Assert.True(AppIdentity.IsStoreApp(SettingsApp));
        Assert.True(AppIdentity.Exists(SettingsApp));
        var name = AppIdentity.DisplayName(SettingsApp);
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain("immersivecontrolpanel", name); // a real display name, not the raw AUMID
        Assert.NotNull(ShellIcon.Get(SettingsApp));
        Assert.False(AppIdentity.Exists(@"shell:AppsFolder\not.a.real.app!nope"));
    }

    [Fact]
    public void DesktopApp_KeyIsExePath_AndFrameHostWindowsResolveToStoreKeys()
    {
        var explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.Equal(explorer, AppIdentity.Key(IntPtr.Zero, explorer));
        Assert.False(AppIdentity.IsStoreApp(explorer));
        Assert.Null(AppIdentity.AumidOf(IntPtr.Zero));
    }
}
