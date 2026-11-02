using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using QuickNotesPlugin;
using ThemeStudioPlugin;
using XNotch.Helpers;
using XNotch.Plugins;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class UpdateServiceAndPluginsTests
{
    [Fact]
    public void UpdateService_DefaultConstructor_InitializesWithCurrentVersionAndEndpoints()
    {
        var updater = new UpdateService();

        Assert.NotNull(updater.CurrentVersion);
        Assert.False(updater.IsUpdateAvailable); // No check executed yet
        Assert.Null(updater.LatestVersion);
        Assert.Null(updater.InstallerDownloadUrl);
    }

    [Fact]
    public void UpdateService_CustomRepository_InitializesCorrectly()
    {
        var updater = new UpdateService("custom-user/custom-repo");

        Assert.Equal(AppInfo.Version, updater.CurrentVersion);
    }

    [Theory]
    [InlineData("0.7.0", "0.7.1", true)]
    [InlineData("0.7.0", "0.8.0", true)]
    [InlineData("0.7.0", "1.0.0", true)]
    [InlineData("0.7.2", "0.7.2", false)]
    [InlineData("0.7.2", "0.7.1", false)]
    [InlineData("0.7.2", "invalid", false)]
    public void VersionComparison_SemanticRules_MatchExpected(string current, string remote, bool expectedUpdateAvailable)
    {
        bool hasUpdate = Version.TryParse(remote, out var r)
            && Version.TryParse(current, out var l)
            && r > l;

        Assert.Equal(expectedUpdateAvailable, hasUpdate);
    }

    [Theory]
    [InlineData("v0.7.3", "0.7.3")]
    [InlineData("V1.0.0", "1.0.0")]
    [InlineData("2.1.4", "2.1.4")]
    public void VersionTrimming_StripsLeadingV(string rawTag, string expectedVersion)
    {
        var clean = rawTag.TrimStart('v', 'V');
        Assert.Equal(expectedVersion, clean);
    }

    [Fact]
    public void ThemeStudioPlugin_MetadataAndPresets_AreValid()
    {
        var plugin = new ThemeStudioPlugin.ThemeStudioPlugin();

        Assert.Equal("com.xnotch.themestudio", plugin.Id);
        Assert.Equal("Theme Studio", plugin.Name);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.False(string.IsNullOrWhiteSpace(plugin.Description));
        Assert.True(ThemeStudioPlugin.ThemeStudioPlugin.Presets.Count >= 5);

        // Verify all presets have valid hex colors
        foreach (var preset in ThemeStudioPlugin.ThemeStudioPlugin.Presets)
        {
            Assert.StartsWith("#", preset.AccentHex);
            Assert.StartsWith("#", preset.BackgroundHex);
            Assert.StartsWith("#", preset.TextHex);
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Icon));
        }
    }

    [Fact]
    public void ThemeStudioPlugin_GetConfigurationDefinition_ReturnsValidDefinition()
    {
        var plugin = new ThemeStudioPlugin.ThemeStudioPlugin();
        var config = plugin.GetConfigurationDefinition();

        Assert.NotNull(config);
        Assert.Equal("Theme Studio Configuration", config.Title);
        Assert.NotEmpty(config.Sections);
        Assert.NotEmpty(config.Sections[0].Fields);
        Assert.Equal(ThemeStudioPlugin.ThemeStudioPlugin.Presets.Count, config.Sections[0].Fields[0].Options.Count);
    }

    [Fact]
    public void QuickNotesPlugin_Metadata_IsValid()
    {
        var plugin = new QuickNotesPlugin.QuickNotesPlugin();

        Assert.Equal("com.xnotch.quicknotes", plugin.Id);
        Assert.Equal("Quick Notes", plugin.Name);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.False(string.IsNullOrWhiteSpace(plugin.Description));
    }

    [Fact]
    public void QuickNotesPlugin_CalculateCountersText_CountsCorrectly()
    {
        var text = "Hello world this is a test note";
        var result = QuickNotesPlugin.QuickNotesPlugin.CalculateCountersText(text);

        Assert.Contains("7 words", result);
        Assert.Contains("31 chars", result);
    }

    [Fact]
    public void QuickNotesPlugin_CalculateCountersText_HandlesEmptyGracefully()
    {
        var result = QuickNotesPlugin.QuickNotesPlugin.CalculateCountersText("");
        Assert.Equal("0 words | 0 chars", result);
    }

    [Fact]
    public void QuickNotesPlugin_GetConfigurationDefinition_HasOptions()
    {
        var plugin = new QuickNotesPlugin.QuickNotesPlugin();
        var config = plugin.GetConfigurationDefinition();

        Assert.NotNull(config);
        Assert.Equal("Quick Notes Configuration", config.Title);
        Assert.NotEmpty(config.Sections);
        Assert.Equal(4, config.Sections[0].Fields[0].Options.Count);
    }

    [Fact]
    public void PluginManifest_NewPlugins_DeserializeProperly()
    {
        var repoRoot = FindRepoRoot();
        var themeManifestJson = File.ReadAllText(
            Path.Combine(repoRoot, "Examples", "Plugins", "ThemeStudioPlugin", "manifest.json"));
        var themeManifest = JsonSerializer.Deserialize<PluginManifest>(themeManifestJson);

        Assert.NotNull(themeManifest);
        Assert.Equal("com.xnotch.themestudio", themeManifest.Id);
        Assert.Equal(PluginCategory.Theme, themeManifest.Category);
        Assert.Equal("0.7.0", themeManifest.MinimumXNotchVersion);

        var notesManifestJson = File.ReadAllText(
            Path.Combine(repoRoot, "Examples", "Plugins", "QuickNotesPlugin", "manifest.json"));
        var notesManifest = JsonSerializer.Deserialize<PluginManifest>(notesManifestJson);

        Assert.NotNull(notesManifest);
        Assert.Equal("com.xnotch.quicknotes", notesManifest.Id);
        Assert.Equal(PluginCategory.Productivity, notesManifest.Category);
        Assert.Equal("0.7.0", notesManifest.MinimumXNotchVersion);
    }

    [Fact]
    public void ExampleLibraryJson_ContainsAllOfficialPlugins()
    {
        var repoRoot = FindRepoRoot();
        var libraryJsonPath = Path.Combine(repoRoot, "Examples", "Plugins", "example-library.json");
        var json = File.ReadAllText(libraryJsonPath);
        var library = JsonSerializer.Deserialize<PluginLibraryService.PluginLibrary>(json);

        Assert.NotNull(library);
        Assert.NotNull(library.Plugins);
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.themestudio");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.quicknotes");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.betteranimation");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.todopeek");
    }

    [Fact]
    public void UIPluginLocation_Overlay_IsMarkedObsolete()
    {
        var member = typeof(UIPluginLocation).GetMember(nameof(UIPluginLocation.Overlay));
        Assert.NotEmpty(member);
        var obsoleteAttr = member[0].GetCustomAttributes(typeof(ObsoleteAttribute), false);
        Assert.NotEmpty(obsoleteAttr);
    }

    [Fact]
    public void PluginLibraryService_LoadBundledLibraryJson_ReturnsValidCatalog()
    {
        var json = PluginLibraryService.LoadBundledLibraryJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var library = JsonSerializer.Deserialize<PluginLibraryService.PluginLibrary>(json!);
        Assert.NotNull(library);
        Assert.NotNull(library.Plugins);
        Assert.NotEmpty(library.Plugins);
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.themestudio");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.quicknotes");
    }

    [Fact]
    public async Task PluginLibraryService_RefreshLibraryAsync_FallsBackToBundledCatalog()
    {
        var service = new PluginLibraryService();
        // Point to an invalid custom URL to force fallback
        var success = await service.RefreshLibraryAsync("http://127.0.0.1:54321/nonexistent.json");

        Assert.True(success);
        Assert.NotEmpty(service.AvailablePlugins);
        Assert.Contains(service.AvailablePlugins, p => p.Id == "com.xnotch.themestudio");
        Assert.Contains(service.AvailablePlugins, p => p.Id == "com.xnotch.quicknotes");
    }

    [Fact]
    public void PluginLibraryService_FindLocalPluginDll_LocatesBuiltPlugins()
    {
        var themeManifest = new PluginManifest
        {
            Id = "com.xnotch.themestudio",
            Name = "Theme Studio",
            DownloadUrl = "https://example.com/ThemeStudioPlugin.dll"
        };

        var localDll = PluginLibraryService.FindLocalPluginDll(themeManifest);
        Assert.NotNull(localDll);
        Assert.True(File.Exists(localDll));
    }

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "X-Notch.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "X-Notch.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return @"P:\Reverse engineering applications\X-Notch";
    }
}
