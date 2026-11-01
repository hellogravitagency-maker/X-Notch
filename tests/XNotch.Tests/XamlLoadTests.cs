using System;
using System.Threading;
using System.Windows;
using XNotch.Models;
using XNotch.Services;
using XNotch.Views;
using XNotch.Views.Panels;
using Xunit;

namespace XNotch.Tests;

[Collection("WpfSequential")]
public class XamlLoadTests
{
    private static readonly object AppLock = new();

    private static Application EnsureApplication()
    {
        lock (AppLock)
        {
            if (Application.Current != null) return Application.Current;
            try { return new Application(); } catch (InvalidOperationException) { return Application.Current!; }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = EnsureApplication();
                var settings = new AppSettings();
                var themeService = new ThemeService();
                themeService.Apply(settings);
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
        {
            Console.WriteLine("CAUGHT EXCEPTION: " + caught.ToString());
            Assert.Null(caught);
        }
    }

    [Fact]
    public void NotchWindow_CanBeInstantiated()
    {
        RunOnSta(() =>
        {
            var settings = new AppSettings();
            var themeService = new ThemeService();
            themeService.Apply(settings);
            var notchServices = new NotchServices();
            var window = new NotchWindow(settings, themeService, notchServices, true, null);
            Assert.NotNull(window);
        });
    }

    [Fact]
    public void SpotlightLauncher_CanBeInstantiated()
    {
        RunOnSta(() =>
        {
            var launcher = new SpotlightLauncher();
            Assert.NotNull(launcher);
        });
    }

    [Fact]
    public void CarouselWidgets_CanBeInstantiated()
    {
        RunOnSta(() =>
        {
            var tasksWidget = new TasksWidgetView();
            Assert.NotNull(tasksWidget);

            var waterWidget = new WaterTrackerWidgetView();
            Assert.NotNull(waterWidget);

            var sleepWidget = new SleepTrackerWidgetView();
            Assert.NotNull(sleepWidget);

            var stopwatchWidget = new StopwatchWidgetView();
            Assert.NotNull(stopwatchWidget);

            var vitalsWidget = new SystemVitalsWidgetView();
            Assert.NotNull(vitalsWidget);
            using var hw = new HardwareMonitorService();
            using var battery = new BatteryService();
            using var armoury = new ArmouryCrateService();
            vitalsWidget.Bind(hw, battery, armoury);
            armoury.CycleNextOperatingMode();
            vitalsWidget.Dispose();

            var notifWidget = new NotificationHistoryWidgetView();
            Assert.NotNull(notifWidget);
        });
    }

    [Fact]
    public void CoreViews_CanBeInstantiated()
    {
        RunOnSta(() =>
        {
            var calendar = new CalendarView();
            Assert.NotNull(calendar);

            var settingsView = new InlineSettingsView();
            Assert.NotNull(settingsView);

            var clock = new ClockWidget();
            Assert.NotNull(clock);

            var battery = new BatteryIndicator();
            Assert.NotNull(battery);

            var musicLive = new MusicLiveActivity();
            Assert.NotNull(musicLive);

            var musicPlayer = new MusicPlayerView();
            Assert.NotNull(musicPlayer);

            var commandCenter = new ExpandedCommandCenterView();
            Assert.NotNull(commandCenter);
        });
    }
}
