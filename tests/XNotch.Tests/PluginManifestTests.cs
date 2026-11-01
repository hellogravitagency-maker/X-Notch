using System.Text.Json;
using XNotch.Plugins;
using Xunit;

namespace XNotch.Tests;

public class PluginManifestTests
{
    [Theory]
    [InlineData("Fun", PluginCategory.Fun)]
    [InlineData("fun", PluginCategory.Fun)]
    [InlineData("System Utility", PluginCategory.SystemUtility)]
    [InlineData("Widget", PluginCategory.Widget)]
    public void DeserializingKnownCategoryAliases_UsesExpectedEnum(string rawCategory, PluginCategory expectedCategory)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            $$"""
            {
              "id": "demo.plugin",
              "name": "Demo Plugin",
              "version": "1.0.0",
              "author": "Gravit",
              "description": "Test plugin",
              "downloadUrl": "https://example.com/plugin.dll",
              "homepage": "https://example.com",
              "category": "{{rawCategory}}"
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal(expectedCategory, manifest!.Category);
    }

    [Fact]
    public void DeserializingUnknownCategory_FallsBackToOther()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            """
            {
              "id": "demo.plugin",
              "name": "Demo Plugin",
              "version": "1.0.0",
              "author": "Gravit",
              "description": "Test plugin",
              "downloadUrl": "https://example.com/plugin.dll",
              "homepage": "https://example.com",
              "category": "UnexpectedCategory"
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal(PluginCategory.Other, manifest!.Category);
    }
}
