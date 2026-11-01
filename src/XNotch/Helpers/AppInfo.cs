using System.Reflection;

namespace XNotch.Helpers;

/// <summary>
/// Single source of truth for the application's version and branding.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "X-Notch";
    public const string Publisher = "Gravit";
    public const string Copyright = "Copyright © Gravit. All rights reserved.";
    public const string Tagline = "Dynamic Island for Windows by Gravit";

    /// <summary>Version as "Major.Minor.Build" (e.g. "0.7.0").</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>Version prefixed with 'v' (e.g. "v0.7.0").</summary>
    public static string DisplayVersion { get; } = "v" + Version;

    /// <summary>Product name plus version (e.g. "X-Notch v0.7.0").</summary>
    public static string NameWithVersion { get; } = ProductName + " " + DisplayVersion;

    private static string ResolveVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";
    }
}
