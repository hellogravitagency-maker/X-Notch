using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace XNotch.Views;

/// <summary>
/// What an app "is" for the dock and switcher. Desktop apps are their exe path; Store/UWP
/// apps (all hosted by ApplicationFrameHost.exe) are "shell:AppsFolder\&lt;AUMID&gt;", which
/// Explorer can resolve for icons, names and launching.
/// </summary>
public static class AppIdentity
{
    public const string StorePrefix = @"shell:AppsFolder\";

    public static bool IsStoreApp(string key) => key.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase);

    public static string Key(IntPtr hwnd, string exePath)
    {
        if (exePath.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) && AumidOf(hwnd) is string aumid)
            return StorePrefix + aumid;
        return exePath;
    }

    public static bool Exists(string key)
    {
        if (!IsStoreApp(key)) return File.Exists(key);
        var iid = typeof(IShellItem).GUID;
        if (SHCreateItemFromParsingName(key, IntPtr.Zero, ref iid, out var item) != 0) return false;
        Marshal.ReleaseComObject(item);
        return true;
    }

    public static string DisplayName(string key)
    {
        if (IsStoreApp(key))
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(key, IntPtr.Zero, ref iid, out var item) == 0)
            {
                try
                {
                    if (item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var p) == 0)
                    {
                        try { return Marshal.PtrToStringUni(p) ?? key; } finally { Marshal.FreeCoTaskMem(p); }
                    }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            return key[StorePrefix.Length..];
        }
        try
        {
            var desc = FileVersionInfo.GetVersionInfo(key).FileDescription;
            if (!string.IsNullOrWhiteSpace(desc)) return desc;
        }
        catch { }
        return Path.GetFileNameWithoutExtension(key);
    }

    public static bool Launch(string key)
    {
        try
        {
            Process.Start(IsStoreApp(key)
                ? new ProcessStartInfo("explorer.exe", key) { UseShellExecute = true }
                : new ProcessStartInfo(key) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>System.AppUserModel.ID of a window, if it has one.</summary>
    public static string? AumidOf(IntPtr hwnd)
    {
        var iid = new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"); // IPropertyStore
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(pv.ptr) : null; }
            finally { PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr ptr; public IntPtr ptr2; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attribs);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore store);

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT pv);
}
