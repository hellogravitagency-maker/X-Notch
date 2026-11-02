using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using QuickNotesPlugin;
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
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.betteranimation");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.todopeek");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.focustimer");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.cyberpunktheme");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.nordtheme");
        Assert.Contains(library.Plugins, p => p.Id == "com.xnotch.sunsettheme");
        Assert.DoesNotContain(library.Plugins, p => p.Id == "com.xnotch.quicknotes");
        Assert.DoesNotContain(library.Plugins, p => p.Id == "com.xnotch.helloworld");
    }

    [Fact]
    public void ThemePlugins_ImplementIThemePlugin_AndReturnDistinctPalettes()
    {
        var cyberpunk = new CyberpunkThemePlugin.CyberpunkThemePlugin();
        var nord = new NordThemePlugin.NordThemePlugin();
        var sunset = new SunsetThemePlugin.SunsetThemePlugin();

        Assert.IsAssignableFrom<IThemePlugin>(cyberpunk);
        Assert.IsAssignableFrom<IThemePlugin>(nord);
        Assert.IsAssignableFrom<IThemePlugin>(sunset);

        var cyberPalette = cyberpunk.GetThemePalette();
        var nordPalette = nord.GetThemePalette();
        var sunsetPalette = sunset.GetThemePalette();

        Assert.NotNull(cyberPalette);
        Assert.NotNull(nordPalette);
        Assert.NotNull(sunsetPalette);

        Assert.NotEqual(cyberPalette.Accent, nordPalette.Accent);
        Assert.NotEqual(cyberPalette.Background, sunsetPalette.Background);
        Assert.NotEqual(nordPalette.Accent, sunsetPalette.Accent);
    }

    [Fact]
    public void PluginLibraryService_EmbeddedOrLocalPlugins_ExtractOrFindSuccessfully()
    {
        var manifest = new PluginManifest
        {
            Id = "com.xnotch.cyberpunktheme",
            Name = "Cyberpunk Neon Theme",
            Version = "1.0.0"
        };

        var localDll = PluginLibraryService.FindLocalPluginDll(manifest);
        var tempFile = Path.Combine(Path.GetTempPath(), "xnotch_test_theme.dll");
        try
        {
            var extracted = PluginLibraryService.TryExtractEmbeddedPlugin(manifest, tempFile);
            Assert.True(!string.IsNullOrEmpty(localDll) || extracted);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AllPluginsInLibrary_CanBeResolvedLocallyOrEmbedded()
    {
        var repoRoot = FindRepoRoot();
        var libraryJsonPath = Path.Combine(repoRoot, "src", "XNotch", "Assets", "example-library.json");
        var json = File.ReadAllText(libraryJsonPath);
        var library = JsonSerializer.Deserialize<PluginLibraryService.PluginLibrary>(json);

        Assert.NotNull(library);
        Assert.NotEmpty(library.Plugins);

        foreach (var plugin in library.Plugins)
        {
            var localDll = PluginLibraryService.FindLocalPluginDll(plugin);
            var tempFile = Path.Combine(Path.GetTempPath(), $"xnotch_test_{plugin.Id}.dll");
            try
            {
                var extracted = PluginLibraryService.TryExtractEmbeddedPlugin(plugin, tempFile);
                Assert.True(
                    !string.IsNullOrWhiteSpace(localDll) || extracted,
                    $"Plugin {plugin.Id} ({plugin.Name}) could not be resolved from local paths or embedded resources!");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void UIPluginLocation_Overlay_IsMarkedObsolete()
    {
#pragma warning disable CS0618
        var member = typeof(UIPluginLocation).GetMember(nameof(UIPluginLocation.Overlay));
#pragma warning restore CS0618
        Assert.NotEmpty(member);
        var obsoleteAttr = member[0].GetCustomAttributes(typeof(ObsoleteAttribute), false);
        Assert.NotEmpty(obsoleteAttr);
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
