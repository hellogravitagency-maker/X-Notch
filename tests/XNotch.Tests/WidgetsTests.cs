using System;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class WidgetsTests
{
    [Theory]
    [InlineData("USD/EUR", "USD", "EUR")]
    [InlineData("usd inr", "USD", "INR")]
    [InlineData("GBP→JPY", "GBP", "JPY")]
    public void ParsePair_Accepts(string input, string from, string to) =>
        Assert.Equal((from, to), WidgetsService.ParsePair(input));

    [Theory]
    [InlineData("")]
    [InlineData("dollars to euros")]
    [InlineData("US/EURO")]
    public void ParsePair_Rejects(string input) => Assert.Null(WidgetsService.ParsePair(input));

    [Fact]
    public void Countdown_Words()
    {
        var now = new DateTime(2026, 11, 22, 15, 0, 0);
        Assert.Equal("today", WidgetsService.CountdownText("2026-11-22", now));
        Assert.Equal("tomorrow", WidgetsService.CountdownText("2026-11-23", now));
        Assert.Equal("33 days", WidgetsService.CountdownText("2026-12-25", now));
        Assert.Equal("2 days ago", WidgetsService.CountdownText("2026-11-20", now));
        Assert.Null(WidgetsService.CountdownText("someday", now));
    }

    [Fact]
    public void WorldClock_FindsZoneByCityOrId()
    {
        Assert.NotNull(WidgetsService.FindZone("UTC"));
        Assert.Contains("Tokyo", WidgetsService.FindZone("Tokyo")!.DisplayName);
        Assert.Null(WidgetsService.FindZone("Atlantis-Under-The-Sea"));
    }

    [Fact]
    public void Pet_FollowsIdleTime()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 10);
        Assert.Equal(PetAction.Walk, WidgetsService.PetFor(TimeSpan.Zero, t));
        Assert.Equal(PetAction.Sit, WidgetsService.PetFor(TimeSpan.FromMinutes(2), t));
        Assert.Equal(PetAction.Sleep, WidgetsService.PetFor(TimeSpan.FromMinutes(10), t));
    }

    [Fact]
    public void Weather_And_Aqi_Labels()
    {
        Assert.Equal("Clear", WidgetsService.DescribeWeather(0).Text);
        Assert.Equal("Rain", WidgetsService.DescribeWeather(63).Text);
        Assert.Equal("Storm", WidgetsService.DescribeWeather(95).Text);
        Assert.Equal("Good", WidgetsService.AqiBand(20));
        Assert.Equal("Unhealthy", WidgetsService.AqiBand(180));
    }
}
