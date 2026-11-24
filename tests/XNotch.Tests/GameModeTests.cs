using System.Collections.Generic;
using System.Text.Json;
using XNotch.Models;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class GameModeTests
{
    [Fact]
    public void Resolve_PerAppRuleBeatsFullscreenRule()
    {
        var rules = new Dictionary<string, AppRule> { ["valorant.exe"] = AppRule.Game, ["chrome.exe"] = AppRule.Slim };
        Assert.Equal(AppRule.Game, AppRules.Resolve(rules, "VALORANT.exe", fullscreenHere: false, AppRule.Hide));
        Assert.Equal(AppRule.Slim, AppRules.Resolve(rules, "chrome.exe", fullscreenHere: true, AppRule.Hide));
        Assert.Equal(AppRule.Hide, AppRules.Resolve(rules, "vlc.exe", fullscreenHere: true, AppRule.Hide));
        Assert.Equal(AppRule.Normal, AppRules.Resolve(rules, "vlc.exe", fullscreenHere: false, AppRule.Game));
        Assert.Equal(AppRule.Normal, AppRules.Resolve(rules, null, fullscreenHere: false, AppRule.Game));
    }

    [Fact]
    public void Rules_RoundTripThroughSettingsJson()
    {
        var s = new AppSettings { FullscreenRule = AppRule.Game };
        s.AppRules["game.exe"] = AppRule.Hide;
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(AppRule.Game, back.FullscreenRule);
        Assert.Equal(AppRule.Hide, back.AppRules["game.exe"]);
        Assert.Contains("\"Hide\"", JsonSerializer.Serialize(s)); // readable enum names on disk
    }

    [Fact]
    public void Fps_StartsOrReportsPermission_ThenStops()
    {
        using var fps = new FpsService();
        fps.Start();
        Assert.True(fps.State is FpsState.Running or FpsState.NeedsPermission, $"state was {fps.State}");
        fps.Stop();
        Assert.Equal(FpsState.Off, fps.State);
        Assert.Null(fps.FpsOf(1234));
    }
}
