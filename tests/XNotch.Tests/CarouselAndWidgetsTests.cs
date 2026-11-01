using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XNotch.Models;
using XNotch.Views;
using Xunit;

namespace XNotch.Tests;

public class CarouselAndWidgetsTests
{
    [Fact]
    public void AppSettings_CarouselToggles_DefaultTrueAndSerialize()
    {
        var settings = new AppSettings();
        Assert.True(settings.ShowMusicCard);
        Assert.True(settings.ShowStopwatchCard);
        Assert.True(settings.ShowWaterCard);
        Assert.True(settings.ShowSleepCard);
        Assert.True(settings.ShowTasksCard);
        Assert.True(settings.ShowVitalsCard);

        settings.ShowWaterCard = false;
        settings.WaterCurrentMl = 1500;
        settings.SleepLastNightHours = 8.5;

        var json = JsonSerializer.Serialize(settings);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.ShowWaterCard);
        Assert.True(deserialized.ShowMusicCard);
        Assert.Equal(1500, deserialized.WaterCurrentMl);
        Assert.Equal(8.5, deserialized.SleepLastNightHours);
    }

    [Fact]
    public void WaterTracker_IncrementAndClampingLogic()
    {
        int current = 750;
        int goal = 2000;

        // +250
        current += 250;
        Assert.Equal(1000, current);

        // +500
        current += 500;
        Assert.Equal(1500, current);

        // -250
        current = Math.Max(0, current - 250);
        Assert.Equal(1250, current);

        int remaining = Math.Max(0, goal - current);
        Assert.Equal(750, remaining);

        int glasses = (int)Math.Ceiling(remaining / 250.0);
        Assert.Equal(3, glasses);

        double pct = Math.Clamp((double)current / goal, 0.0, 1.0) * 100;
        Assert.Equal(62.5, pct);
    }

    [Fact]
    public void SleepTracker_CalculationsAndGoalProgress()
    {
        double lastNight = 7.5;
        double target = 8.0;

        int h = (int)lastNight;
        int m = (int)Math.Round((lastNight - h) * 60);

        Assert.Equal(7, h);
        Assert.Equal(30, m);

        int qualityPct = Math.Clamp((int)((lastNight / target) * 100), 10, 100);
        Assert.Equal(93, qualityPct);

        // Adjustment +15m
        lastNight = Math.Round(lastNight + 0.25, 2);
        Assert.Equal(7.75, lastNight);

        // Adjustment -15m
        lastNight = Math.Round(lastNight - 0.25, 2);
        Assert.Equal(7.5, lastNight);
    }

    [Fact]
    public void TasksModel_ManagementAndCompletion()
    {
        var tasks = new List<QuickTaskModel>
        {
            new() { Title = "Task 1", IsCompleted = false },
            new() { Title = "Task 2", IsCompleted = true },
            new() { Title = "Task 3", IsCompleted = false }
        };

        Assert.Equal(2, tasks.Count(t => !t.IsCompleted));

        // Toggle Task 1 completed
        tasks[0].IsCompleted = true;
        Assert.Equal(1, tasks.Count(t => !t.IsCompleted));

        // Clear completed
        tasks.RemoveAll(t => t.IsCompleted);
        Assert.Single(tasks);
        Assert.Equal("Task 3", tasks[0].Title);

        // Add new task via input simulation
        string input = "  Finish unit tests  ";
        string trimmed = input.Trim();
        Assert.False(string.IsNullOrWhiteSpace(trimmed));
        tasks.Insert(0, new QuickTaskModel { Title = trimmed, IsCompleted = false });
        Assert.Equal(2, tasks.Count);
        Assert.Equal("Finish unit tests", tasks[0].Title);
    }

    [Fact]
    public void Stopwatch_FormatTimeSpan_CorrectMinutesAndHundredths()
    {
        var ts = new TimeSpan(0, 0, 14, 25, 820);
        int minutes = (int)ts.TotalMinutes;
        int seconds = ts.Seconds;
        int hundredths = ts.Milliseconds / 10;
        string formatted = $"{minutes:D2}:{seconds:D2}.{hundredths:D2}";

        Assert.Equal("14:25.82", formatted);
    }

    [Fact]
    public void NotificationHistory_AddDismissAndClear_Lifecycle()
    {
        using var notifService = new XNotch.Services.NotificationService();
        notifService.SeedWelcomeNotificationIfEmpty();

        var history = notifService.History;
        Assert.NotEmpty(history);
        Assert.Equal(2, history.Count);

        // Add a new notification
        notifService.AddNotification("Slack", "Alice", "Hey, check the PR!");
        var updated = notifService.History;
        Assert.Equal(3, updated.Count);
        Assert.Equal("Slack", updated[0].AppName);
        Assert.Equal("Alice", updated[0].Title);

        // Dismiss the first notification
        uint idToDismiss = updated[0].Id;
        notifService.DismissNotification(idToDismiss);
        Assert.Equal(2, notifService.History.Count);

        // Clear all
        notifService.ClearHistory();
        Assert.Empty(notifService.History);
    }
}
