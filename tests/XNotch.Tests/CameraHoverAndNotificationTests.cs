using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class CameraHoverAndNotificationTests
{
    [Fact]
    public void CameraBezelHelper_IsInCameraZone_PrimaryDisplay_DetectsCenterAndBoundaries()
    {
        // 1920x1080 display at origin (0,0). Center X is 960.
        var screen = new Rectangle(0, 0, 1920, 1080);

        // Dead center right under camera
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(960, 10), screen));

        // Exact left boundary (960 - 135 = 825)
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(825, 20), screen));

        // Exact right boundary (960 + 135 = 1095)
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(1095, 20), screen));

        // Top edge (Y = 0) and bottom edge of bezel depth (Y = 38)
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(960, 0), screen));
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(960, 38), screen));

        // Just outside horizontal boundary
        Assert.False(CameraBezelHelper.IsInCameraZone(new Point(824, 20), screen));
        Assert.False(CameraBezelHelper.IsInCameraZone(new Point(1096, 20), screen));

        // Just below vertical depth
        Assert.False(CameraBezelHelper.IsInCameraZone(new Point(960, 39), screen));

        // Outside screen entirely
        Assert.False(CameraBezelHelper.IsInCameraZone(new Point(960, -5), screen));
        Assert.False(CameraBezelHelper.IsInCameraZone(new Point(2000, 10), screen));
    }

    [Fact]
    public void CameraBezelHelper_IsInCameraZone_SecondaryMonitor_DetectsCorrectOffset()
    {
        // Multi-monitor: secondary 2560x1440 display placed to the right of primary at (1920, 0).
        // Center X = 1920 + 2560 / 2 = 3200.
        var secondaryScreen = new Rectangle(1920, 0, 2560, 1440);

        // Directly under secondary camera
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(3200, 15), secondaryScreen));

        // Within boundaries on secondary screen
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(3200 - 135, 30), secondaryScreen));
        Assert.True(CameraBezelHelper.IsInCameraZone(new Point(3200 + 135, 30), secondaryScreen));

        // Primary monitor cursor should return false for secondary screen bounds
        Assert.False(CameraBezelHelper.IsInCameraZone(new Point(960, 15), secondaryScreen));
    }

    [Fact]
    public void AppSettings_CameraHoverPopUp_DefaultsAndSerialization_RoundtripsProperly()
    {
        var settings = new AppSettings();

        // Verify default configuration
        Assert.True(settings.CameraHoverPopUp);
        Assert.Equal(1800, settings.CameraPopUpDurationMs);

        // Mutate and serialize to JSON
        settings.CameraHoverPopUp = false;
        settings.CameraPopUpDurationMs = 2500;

        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.False(restored.CameraHoverPopUp);
        Assert.Equal(2500, restored.CameraPopUpDurationMs);
    }

    [Fact]
    public void NotificationItem_PropertiesAndFormatting_ArePreserved()
    {
        var timestamp = DateTime.UtcNow;
        var item = new NotificationItem(
            Id: 1042,
            AppName: "Slack",
            Title: "Dev Team",
            Body: "Build succeeded for commit ff36e30",
            Timestamp: timestamp,
            AppUserModelId: "Slack.App"
        );

        Assert.Equal(1042u, item.Id);
        Assert.Equal("Slack", item.AppName);
        Assert.Equal("Dev Team", item.Title);
        Assert.Equal("Build succeeded for commit ff36e30", item.Body);
        Assert.Equal(timestamp, item.Timestamp);
        Assert.Equal("Slack.App", item.AppUserModelId);
    }

    [Fact]
    public void SmartReminderEngine_InactiveProjects_GeneratesNudgeForUncommittedWork()
    {
        using var reminderEngine = new SmartReminderEngine();

        var projects = new[]
        {
            new ProjectRecord(
                Id: "proj-1",
                Name: "X-Notch",
                Path: @"P:\Reverse engineering applications\X-Notch",
                GitBranch: "main",
                HasUncommittedChanges: true,
                LastModified: DateTime.UtcNow.AddDays(-5), // 5 days inactive with changes
                TechStack: "C#, WPF"
            ),
            new ProjectRecord(
                Id: "proj-2",
                Name: "Clean-Repo",
                Path: @"P:\Projects\Clean",
                GitBranch: "main",
                HasUncommittedChanges: false,
                LastModified: DateTime.UtcNow.AddDays(-10),
                TechStack: "Rust"
            )
        };

        reminderEngine.EvaluateInactiveProjects(projects);

        var activeReminders = reminderEngine.ActiveReminders;
        Assert.Single(activeReminders);
        Assert.Contains("X-Notch", activeReminders[0].Title);
        Assert.False(activeReminders[0].IsDismissed);

        // Dismiss reminder
        reminderEngine.DismissReminder(activeReminders[0].Id);
        Assert.Empty(reminderEngine.ActiveReminders);
    }

    [Fact]
    public void NotificationVisualResolver_ResolvesAppColorsAndGlyphsCorrectly()
    {
        // 1. WhatsApp -> Emerald Green + 💬
        var wa = NotificationVisualResolver.Resolve(new NotificationItem(1, "WhatsApp", "Alice", "Hey there!", DateTime.UtcNow));
        Assert.Equal("💬", wa.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x25, 0xD3, 0x66), wa.AccentColor);

        // 2. Outlook -> Blue + ✉️
        var outlook = NotificationVisualResolver.Resolve(new NotificationItem(2, "Outlook", "Meeting invitation", "Quarterly Sync", DateTime.UtcNow));
        Assert.Equal("✉️", outlook.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4), outlook.AccentColor);

        // 3. Calendar -> Red + 📅
        var cal = NotificationVisualResolver.Resolve(new NotificationItem(3, "Google Calendar", "Team Standup", "Starting in 5m", DateTime.UtcNow));
        Assert.Equal("📅", cal.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0x3B, 0x30), cal.AccentColor);

        // 4. Reminders / To-Do -> Orange + ☑️
        var todo = NotificationVisualResolver.Resolve(new NotificationItem(4, "Microsoft To Do", "Submit PR", "Due today", DateTime.UtcNow));
        Assert.Equal("☑️", todo.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0x95, 0x00), todo.AccentColor);

        // 5. Spotify -> Spotify Green + 🎵
        var spotify = NotificationVisualResolver.Resolve(new NotificationItem(5, "Spotify", "Now Playing", "Artist - Track", DateTime.UtcNow));
        Assert.Equal("🎵", spotify.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x1D, 0xB9, 0x54), spotify.AccentColor);

        // 6. GitHub -> Royal Purple + 🐙
        var github = NotificationVisualResolver.Resolve(new NotificationItem(6, "GitHub Desktop", "Pull Request Merged", "#42 feat: dynamic island", DateTime.UtcNow));
        Assert.Equal("🐙", github.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xAF, 0x52, 0xDE), github.AccentColor);

        // 7. Coding Agent / Claude -> Radiant Amber + ⚡
        var claude = NotificationVisualResolver.Resolve(new NotificationItem(7, "Claude Code", "Task Finished", "All unit tests passing", DateTime.UtcNow));
        Assert.Equal("⚡", claude.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A), claude.AccentColor);

        // 8. General fallback -> Apple iOS Accent Blue + 🔔
        var generic = NotificationVisualResolver.Resolve(new NotificationItem(8, "Random Utility", "Notice", "Background sync complete", DateTime.UtcNow));
        Assert.Equal("🔔", generic.Glyph);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x0A, 0x84, 0xFF), generic.AccentColor);
    }

    [Fact]
    public void AppLogoResolver_ResolvesOriginalBrandAppLogos()
    {
        var spotify = NotificationVisualResolver.Resolve(new NotificationItem(1, "Spotify", "Title", "Body", DateTime.UtcNow));
        Assert.NotNull(spotify.AppLogoSource);

        var wa = NotificationVisualResolver.Resolve(new NotificationItem(2, "WhatsApp", "Title", "Body", DateTime.UtcNow));
        Assert.NotNull(wa.AppLogoSource);

        var discord = NotificationVisualResolver.Resolve(new NotificationItem(3, "Discord", "Title", "Body", DateTime.UtcNow));
        Assert.NotNull(discord.AppLogoSource);

        var gh = NotificationVisualResolver.Resolve(new NotificationItem(4, "GitHub Desktop", "Title", "Body", DateTime.UtcNow));
        Assert.NotNull(gh.AppLogoSource);

        var claude = NotificationVisualResolver.Resolve(new NotificationItem(5, "Claude Code", "Title", "Body", DateTime.UtcNow));
        Assert.NotNull(claude.AppLogoSource);

        var slack = NotificationVisualResolver.Resolve(new NotificationItem(6, "Slack", "Title", "Body", DateTime.UtcNow));
        Assert.NotNull(slack.AppLogoSource);

        var chrome = NotificationVisualResolver.Resolve(new NotificationItem(7, "Google Chrome", "Title", "Body", DateTime.UtcNow));
        Assert.NotNull(chrome.AppLogoSource);
    }
}
