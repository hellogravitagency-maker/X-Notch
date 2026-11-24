using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using XNotch.Helpers;
using Xunit;

namespace XNotch.Tests;

public class LocalizationTests
{
    [Fact]
    public void EveryShippedLanguage_Loads_AndKeepsPlaceholders()
    {
        foreach (var (code, _) in Loc.Languages.Where(l => l.Code != "en"))
        {
            var table = Loc.Load(code);
            Assert.True(table is { Count: > 200 }, $"{code} missing or tiny");
            foreach (var (en, tr) in table!)
            {
                Assert.False(string.IsNullOrWhiteSpace(tr), $"{code}: empty translation for {en}");
                Assert.Equal(Holes(en), Holes(tr));
            }
        }
    }

    [Fact]
    public void T_FallsBackToEnglish_AndFormats()
    {
        Assert.Equal("Some brand new string", Loc.T("Some brand new string"));
        Assert.Equal("Claude needs you", Loc.T("{0} needs you", "Claude"));
    }

    [Theory]
    [InlineData("auto", "ja-JP", "ja")]
    [InlineData("auto", "zh-CN", "zh-Hans")]
    [InlineData("auto", "zh-TW", "en")]   // Traditional Chinese not shipped yet
    [InlineData("auto", "sv-SE", "en")]
    [InlineData("auto", "hi-IN", "hi")]
    [InlineData("de", "en-US", "de")]     // explicit choice beats Windows
    [InlineData("xx", "fr-FR", "fr")]     // unknown code → follow Windows
    public void Resolve_PicksLanguage(string setting, string windows, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, new CultureInfo(windows)));

    private static string[] Holes(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToArray();
}
