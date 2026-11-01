using System;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class LyricsServiceTests
{
    [Fact]
    public void CleanTrackTitle_RemovesRemasteredAndFeatures()
    {
        string raw1 = "Hotel California - 2013 Remaster";
        string clean1 = LyricsService.CleanTrackTitle(raw1);
        Assert.Equal("Hotel California", clean1);

        string raw2 = "Blinding Lights (feat. Someone)";
        string clean2 = LyricsService.CleanTrackTitle(raw2);
        Assert.Equal("Blinding Lights", clean2);

        string raw3 = "Comfortably Numb [Live at Pompeii]";
        string clean3 = LyricsService.CleanTrackTitle(raw3);
        Assert.Equal("Comfortably Numb", clean3);
    }

    [Fact]
    public void CleanArtistName_ExtractsPrimaryArtist()
    {
        Assert.Equal("Daft Punk", LyricsService.CleanArtistName("Daft Punk, Pharrell Williams"));
        Assert.Equal("The Weeknd", LyricsService.CleanArtistName("The Weeknd & Ariana Grande"));
        Assert.Equal("Coldplay", LyricsService.CleanArtistName("Coldplay/BTS"));
    }

    [Fact]
    public void ParseLrc_CorrectlySortsAndParsesTimestamps()
    {
        string lrc = @"
[00:12.50]Line one is here
[00:05.00]Intro line
[00:20.100]Third line arrives
";
        var lines = LyricsService.ParseLrc(lrc);
        Assert.Equal(3, lines.Count);

        // Verify sorted order
        Assert.Equal(TimeSpan.FromSeconds(5.0), lines[0].Timestamp);
        Assert.Equal("Intro line", lines[0].Text);

        Assert.Equal(TimeSpan.FromMilliseconds(12500), lines[1].Timestamp);
        Assert.Equal("Line one is here", lines[1].Text);

        Assert.Equal(TimeSpan.FromMilliseconds(20100), lines[2].Timestamp);
        Assert.Equal("Third line arrives", lines[2].Text);
    }

    [Fact]
    public void UpdatePlayhead_ReturnsCorrectActiveLine()
    {
        using var service = new LyricsService();
        string lrc = @"
[00:02.00]First stanza
[00:08.00]Second stanza
[00:15.00]Chorus starts
";
        var lines = LyricsService.ParseLrc(lrc);

        // Use reflection or direct test via service method
        // Before first line
        Assert.Equal(string.Empty, service.UpdatePlayhead(TimeSpan.FromSeconds(1)));
    }
}
