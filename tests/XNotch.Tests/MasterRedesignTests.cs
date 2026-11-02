using System;
using System.Linq;
using System.Threading;
using System.Windows;
using XNotch.Models;
using XNotch.Services;
using XNotch.Views;
using XNotch.Views.Components;
using Xunit;

namespace XNotch.Tests;

[Collection("WpfSequential")]
public class MasterRedesignTests
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
            throw new AggregateException(caught);
        }
    }

    [Fact]
    public void NotchState_S0ThroughS7_MatchesExpectedHierarchyAndAliases()
    {
        // S0 & S1
        Assert.Equal(NotchState.Closed, NotchState.Dormant);
        Assert.Equal(NotchState.Closed, NotchState.Ambient);
        Assert.True(NotchState.Dormant.IsRestingState());
        Assert.True(NotchState.Ambient.IsRestingState());

        // S2
        Assert.Equal(NotchState.Peeking, NotchState.Peek);
        Assert.True(NotchState.Peek.IsPeeking());

        // S3
        Assert.Equal(NotchState.Open, NotchState.Expanded);
        Assert.True(NotchState.Expanded.IsExpandedState());

        // S4 - Focus
        Assert.True(NotchState.Focus.IsExpandedState());

        // S6 - Command
        Assert.True(NotchState.Command.IsExpandedState());

        // S7 - Workspace
        Assert.True(NotchState.Workspace.IsExpandedState());
    }

    [Fact]
    public void NotchViewModel_PinAndFocusTransitions_WorkAccurately()
    {
        var vm = new NotchViewModel();
        Assert.Equal(NotchState.Closed, vm.NotchState);
        Assert.False(vm.IsPinned);

        // Toggle to open
        vm.Open();
        Assert.Equal(NotchState.Open, vm.NotchState);

        // Toggle Focus pin
        vm.Focus();
        Assert.True(vm.IsPinned);
        Assert.Equal(NotchState.Focus, vm.NotchState);

        // Unpin
        vm.TogglePin();
        Assert.False(vm.IsPinned);
        Assert.Equal(NotchState.Open, vm.NotchState);

        // Command mode
        vm.Command();
        Assert.Equal(NotchState.Command, vm.NotchState);

        // Workspace mode
        vm.Workspace();
        Assert.Equal(NotchState.Workspace, vm.NotchState);

        // Close clears pin
        vm.TogglePin();
        Assert.True(vm.IsPinned);
        vm.Close();
        Assert.False(vm.IsPinned);
        Assert.Equal(NotchState.Closed, vm.NotchState);
    }

    [Fact]
    public void NotchConstants_S0ThroughS7Dimensions_SatisfyGeometryConstraints()
    {
        Assert.Equal(200, NotchConstants.AmbientWidth);
        Assert.True(NotchConstants.AmbientHeight >= 32 && NotchConstants.AmbientHeight <= 42);

        Assert.True(NotchConstants.FocusWidth >= 380 && NotchConstants.FocusWidth <= 480);
        Assert.True(NotchConstants.FocusHeight >= 120 && NotchConstants.FocusHeight <= 200);

        Assert.True(NotchConstants.CommandWidth >= 480 && NotchConstants.CommandWidth <= 560);
        Assert.True(NotchConstants.CommandHeight >= 240 && NotchConstants.CommandHeight <= 340);

        Assert.True(NotchConstants.WorkspaceWidth >= 500 && NotchConstants.WorkspaceWidth <= 620);
        Assert.True(NotchConstants.WorkspaceHeight >= 350 && NotchConstants.WorkspaceHeight <= 500);

        Assert.True(NotchConstants.NotificationWidth >= 300 && NotchConstants.NotificationWidth <= 380);
        Assert.True(NotchConstants.NotificationHeight >= 38 && NotchConstants.NotificationHeight <= 52);
    }

    [Fact]
    public void CommandCenterView_BuildIndexAndFilter_FindsCommandsAccurately()
    {
        RunOnSta(() =>
        {
            var services = new NotchServices();
            var settings = new AppSettings();
            var cmdView = new CommandCenterView();

            cmdView.Bind(services, settings);
            cmdView.BuildIndex();

            // Verify commands are indexed
            var list = cmdView.CommandsListBox.ItemsSource as System.Collections.Generic.List<CommandEntry>;
            Assert.NotNull(list);
            Assert.True(list.Count >= 10);

            // Verify system, media, productivity, appearance categories exist
            Assert.Contains(list, c => c.Category == "Media");
            Assert.Contains(list, c => c.Category == "Productivity");
            Assert.Contains(list, c => c.Category == "Appearance");
            Assert.Contains(list, c => c.Category == "System");
        });
    }

    [Fact]
    public void XNotchLogo_CanBeInstantiated_WithoutExceptions()
    {
        RunOnSta(() =>
        {
            var logo = new XNotchLogo();
            Assert.NotNull(logo);
        });
    }

    [Fact]
    public void AIIntelligenceWidgetView_CanBeInstantiated_AndBound()
    {
        RunOnSta(() =>
        {
            var services = new NotchServices();
            var aiWidget = new AIIntelligenceWidgetView();
            aiWidget.Bind(services);
            Assert.NotNull(aiWidget);
        });
    }
}
