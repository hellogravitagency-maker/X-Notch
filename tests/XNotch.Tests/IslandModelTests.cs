using System;
using System.Linq;
using System.Windows;
using XNotch.Helpers;
using XNotch.Island;
using XNotch.Models;
using Xunit;

namespace XNotch.Tests;

internal sealed class FakeClock : IClock
{
    public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(100);
    public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);
}

public class IslandPresentationMachineTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private static ActivityEntry Media(int s = 0) => new("media", ActivityPriority.Media, T0.AddSeconds(s));
    private static ActivityEntry Timer(int s = 0) => new("timer", ActivityPriority.Timer, T0.AddSeconds(s));
    private static ActivityEntry Shelf(int s = 0) => new("shelf", ActivityPriority.Shelf, T0.AddSeconds(s));

    [Fact]
    public void Idle_Compact_Minimal_Expanded()
    {
        var m = new IslandPresentationMachine(new FakeClock());
        Assert.Equal(IslandPresentation.Idle, m.Presentation);

        m.SetActivities([Media()]);
        Assert.Equal(IslandPresentation.Compact, m.Presentation);
        Assert.Equal("media", m.Primary?.Id);

        m.SetActivities([Media(), Timer()]);
        Assert.Equal(IslandPresentation.Minimal, m.Presentation);
        Assert.Equal("timer", m.Primary?.Id);
        Assert.Equal("media", m.Secondary?.Id);

        m.SetInteraction(NotchState.Open);
        Assert.Equal(IslandPresentation.Expanded, m.Presentation);

        m.SetInteraction(NotchState.Closed);
        Assert.Equal(IslandPresentation.Minimal, m.Presentation);
    }

    [Fact]
    public void Peeking_KeepsPresentation()
    {
        var m = new IslandPresentationMachine(new FakeClock());
        m.SetActivities([Media()]);
        m.SetInteraction(NotchState.Peeking);
        Assert.Equal(IslandPresentation.Compact, m.Presentation);
        Assert.True(m.IsPeeking);
    }

    [Fact]
    public void Alert_Overlays_ThenRestoresPrevious()
    {
        var clock = new FakeClock();
        var m = new IslandPresentationMachine(clock);
        m.SetActivities([Media()]);
        m.PostAlert(new IslandAlert("usb", "USB"));
        Assert.Equal(IslandPresentation.Alert, m.Presentation);
        Assert.Equal(IslandPresentation.Compact, m.BasePresentation);

        clock.Advance(2.7);
        m.Update();
        Assert.Equal(IslandPresentation.Compact, m.Presentation);
    }

    [Fact]
    public void Hud_Overlays_AndResetsOnEveryPing()
    {
        var clock = new FakeClock();
        var m = new IslandPresentationMachine(clock);
        m.PingHud();
        Assert.Equal(IslandPresentation.Hud, m.Presentation);
        clock.Advance(1.0);
        m.PingHud();
        clock.Advance(1.0);
        m.Update();
        Assert.Equal(IslandPresentation.Hud, m.Presentation); // 2.0 s total, 1.0 since last ping
        clock.Advance(0.6);
        m.Update();
        Assert.Equal(IslandPresentation.Idle, m.Presentation);
    }

    [Fact]
    public void Spotlight_IsModal_AndRestores()
    {
        var clock = new FakeClock();
        var m = new IslandPresentationMachine(clock);
        m.SetActivities([Media()]);
        m.OpenSpotlight();
        m.PostAlert(new IslandAlert("a", "A"));
        m.PingHud();
        Assert.Equal(IslandPresentation.Spotlight, m.Presentation);
        m.CloseSpotlight();
        Assert.Equal(IslandPresentation.Hud, m.Presentation);
        clock.Advance(3);
        m.Update();
        Assert.Equal(IslandPresentation.Compact, m.Presentation);
    }

    [Fact]
    public void GameMode_SuppressesAlerts_ExceptLowBattery()
    {
        var m = new IslandPresentationMachine(new FakeClock());
        m.SetGameMode(true);
        m.PostAlert(new IslandAlert("usb", "USB"));
        Assert.Equal(IslandPresentation.Idle, m.Presentation);
        m.PostAlert(new IslandAlert("bat", "Low", IsLowBattery: true));
        Assert.Equal(IslandPresentation.Alert, m.Presentation);
        Assert.True(m.Alerts.Current!.IsLowBattery);
    }

    [Fact]
    public void Swap_PromotesSecondary_UntilTopTwoChange()
    {
        var m = new IslandPresentationMachine(new FakeClock());
        m.SetActivities([Media(), Timer()]);
        m.SwapPrimary();
        Assert.Equal("media", m.Primary?.Id);
        Assert.Equal("timer", m.Secondary?.Id);
        m.SwapPrimary();
        Assert.Equal("timer", m.Primary?.Id);

        m.SwapPrimary(); // media pinned
        m.SetActivities([Timer(), Shelf()]); // media ended → pin released
        Assert.Equal("timer", m.Primary?.Id);
    }

    [Fact]
    public void Changed_FiresOnlyOnRealChange()
    {
        var m = new IslandPresentationMachine(new FakeClock());
        int n = 0;
        m.Changed += () => n++;
        m.SetActivities([Media()]);
        m.SetActivities([Media()]);
        Assert.Equal(1, n);
    }
}

public class ActivityPriorityResolverTests
{
    private static readonly DateTime T0 = new(2026, 1, 1);

    [Fact]
    public void OrdersByPriority()
    {
        var order = ActivityPriorityResolver.Order([
            new("shelf", ActivityPriority.Shelf, T0),
            new("agent", ActivityPriority.Agent, T0),
            new("media", ActivityPriority.Media, T0),
            new("watch", ActivityPriority.Stopwatch, T0),
            new("timer", ActivityPriority.Timer, T0),
            new("plug", ActivityPriority.PluginDefault, T0, ActivitySource.Plugin),
        ]).Select(a => a.Id);
        Assert.Equal(["agent", "timer", "watch", "media", "plug", "shelf"], order);
    }

    [Fact]
    public void Ties_MostRecentWins()
    {
        var order = ActivityPriorityResolver.Order([
            new("old", 50, T0),
            new("new", 50, T0.AddMinutes(1)),
        ]);
        Assert.Equal("new", order[0].Id);
    }

    [Fact]
    public void PrimaryEnds_SecondaryPromoted()
    {
        var m = new IslandPresentationMachine(new FakeClock());
        m.SetActivities([new("a", 90, T0), new("b", 70, T0), new("c", 50, T0)]);
        Assert.Equal("c", m.Ordered[2].Id);
        m.SetActivities([new("b", 70, T0), new("c", 50, T0)]);
        Assert.Equal("b", m.Primary?.Id);
        Assert.Equal("c", m.Secondary?.Id);
    }

    [Theory]
    [InlineData(500, 80)]
    [InlineData(-5, 0)]
    [InlineData(40, 40)]
    public void PluginPriority_Clamped(int declared, int effective) =>
        Assert.Equal(effective, ActivityPriorityResolver.EffectivePriority(new("p", declared, T0, ActivitySource.Plugin)));

    [Fact]
    public void Plugin_CannotOutrankAgent()
    {
        var order = ActivityPriorityResolver.Order([
            new("agent", ActivityPriority.Agent, T0),
            new("plug", 1000, T0.AddHours(1), ActivitySource.Plugin),
        ]);
        Assert.Equal("agent", order[0].Id);
    }
}

public class AlertQueueTests
{
    [Fact]
    public void DismissesAfter2_6s()
    {
        var c = new FakeClock();
        var q = new AlertQueue(c);
        q.Post(new IslandAlert("a", "A"));
        c.Advance(2.5); q.Update();
        Assert.NotNull(q.Current);
        c.Advance(0.11); q.Update();
        Assert.Null(q.Current);
    }

    [Fact]
    public void HoverPausesCountdown()
    {
        var c = new FakeClock();
        var q = new AlertQueue(c);
        q.Post(new IslandAlert("a", "A"));
        c.Advance(2.0);
        q.HoverPaused = true;
        c.Advance(10); q.Update();
        Assert.NotNull(q.Current);
        q.HoverPaused = false;
        c.Advance(0.5); q.Update();
        Assert.NotNull(q.Current);
        c.Advance(0.2); q.Update();
        Assert.Null(q.Current);
    }

    [Fact]
    public void DedupesSameKeyWithin1s()
    {
        var c = new FakeClock();
        var q = new AlertQueue(c);
        Assert.True(q.Post(new IslandAlert("k", "1")));
        c.Advance(0.5);
        Assert.False(q.Post(new IslandAlert("k", "2")));
        c.Advance(0.6);
        Assert.True(q.Post(new IslandAlert("k", "3")));
    }

    [Fact]
    public void Fifo_Max3Queued()
    {
        var c = new FakeClock();
        var q = new AlertQueue(c);
        q.Post(new IslandAlert("cur", "cur"));
        foreach (var k in new[] { "1", "2", "3", "4" }) q.Post(new IslandAlert(k, k));
        Assert.Equal(3, q.QueuedCount);

        c.Advance(2.7); q.Update();
        Assert.Equal("2", q.Current?.Key); // "1" was dropped as oldest
        c.Advance(2.7); q.Update();
        Assert.Equal("3", q.Current?.Key);
    }

    [Fact]
    public void GameMode_HoldsFor10s_ThenDrops()
    {
        var c = new FakeClock();
        var q = new AlertQueue(c) { GameMode = true };
        q.Post(new IslandAlert("usb", "USB"));
        Assert.Null(q.Current);
        c.Advance(9); q.Update();
        Assert.Equal(1, q.QueuedCount);
        c.Advance(1.1); q.Update();
        Assert.Equal(0, q.QueuedCount);
        q.GameMode = false;
        Assert.Null(q.Current);
    }

    [Fact]
    public void GameMode_HeldAlertShowsWhenModeEnds()
    {
        var c = new FakeClock();
        var q = new AlertQueue(c) { GameMode = true };
        q.Post(new IslandAlert("usb", "USB"));
        c.Advance(5);
        q.GameMode = false;
        Assert.Equal("usb", q.Current?.Key);
    }
}

public class SpringRetargetTests
{
    [Theory]
    [InlineData(0.05)]
    [InlineData(0.12)]
    [InlineData(0.31)]
    public void Retarget_IsContinuous_InValueAndVelocity(double t)
    {
        var s = new SpringAnimator(0.40, 0.72);
        s.AnimateTo(100, 0);
        s.Tick(t);
        double v = s.CurrentValue, vel = s.CurrentVelocity;

        s.Retarget(30, t);
        Assert.True(s.IsAnimating);
        Assert.True(Math.Abs(s.CurrentValue - v) < 1e-6);
        Assert.True(Math.Abs(s.CurrentVelocity - vel) < 1e-6);

        s.Tick(0);
        Assert.True(Math.Abs(s.CurrentValue - v) < 1e-6);
        Assert.True(Math.Abs(s.CurrentVelocity - vel) < 1e-6);
        Assert.Equal(30, s.TargetValue);
    }

    [Fact]
    public void Retarget_FromRest_StartsAtCurrentValue()
    {
        var s = new SpringAnimator(0.3, 1.0);
        s.Reset(12);
        s.Retarget(40);
        Assert.Equal(12, s.CurrentValue);
        Assert.Equal(0, s.CurrentVelocity);
    }

    [Fact]
    public void AnimationStarted_Raised()
    {
        int n = 0;
        void H() => n++;
        SpringAnimator.AnimationStarted += H;
        try { new SpringAnimator().AnimateTo(1, 0); }
        finally { SpringAnimator.AnimationStarted -= H; }
        Assert.True(n >= 1);
    }

    [Fact]
    public void ExpandActivity_Overshoots_About4Percent()
    {
        var s = new SpringAnimator().Use(IslandSprings.ExpandActivity);
        bool reduced = SpringAnimator.Reduced;
        s.AnimateTo(100, 0);
        double peak = 0;
        for (double t = 0; t < 2; t += 0.002) peak = Math.Max(peak, s.Tick(t));
        if (!reduced) Assert.InRange(peak, 102.5, 105.5);
    }
}

public class GooBridgeGeometryTests
{
    private const double R = IslandMetrics.GooRadius;

    [Fact]
    public void Neck_ExistsBelowBreakDistance()
    {
        Assert.NotNull(GooBridgeGeometry.CreateBridge(new Point(0, 0), R, new Point(R * 2 + 4, 0), R));
        Assert.NotNull(GooBridgeGeometry.CreateBridge(new Point(0, 0), R, new Point(GooBridgeGeometry.BreakDistance(R, R) - 0.5, 0), R));
    }

    [Fact]
    public void Neck_AbsentAboveBreakDistance()
    {
        Assert.Null(GooBridgeGeometry.CreateBridge(new Point(0, 0), R, new Point(GooBridgeGeometry.BreakDistance(R, R) + 0.5, 0), R));
        Assert.Null(GooBridgeGeometry.CreateBridge(new Point(0, 0), R, new Point(0, 0), R)); // coincident: nothing to bridge
    }

    [Theory]
    [InlineData(10)]
    [InlineData(28)]
    [InlineData(36)]
    [InlineData(41)]
    public void Neck_StaysWithinHull(double distance)
    {
        var c1 = new Point(50, 40);
        var c2 = new Point(50 + distance, 40);
        var g = GooBridgeGeometry.CreateBridge(c1, R, c2, R)!;
        var b = g.Bounds;
        var hull = new Rect(c1.X - R, c1.Y - R, distance + 2 * R, 2 * R);
        hull.Inflate(0.5, 0.5);
        Assert.True(hull.Contains(b), $"bounds {b} outside hull {hull}");
    }
}

public class RollingDigitsTests
{
    [Fact]
    public void OnlyChangedDigitsAnimate()
    {
        Assert.Equal([3], RollingDigits.ChangedIndices("4:59", "4:58"));
        Assert.Equal([2, 3], RollingDigits.ChangedIndices("4:50", "4:49"));
        Assert.Equal([0, 2, 3], RollingDigits.ChangedIndices("5:00", "4:59"));
        Assert.Empty(RollingDigits.ChangedIndices("85", "85"));
    }

    [Fact]
    public void LengthChange_RightAligned()
    {
        // "9:59" → "10:00": every column changes; the new leading column too.
        Assert.Equal([0, 1, 3, 4], RollingDigits.ChangedIndices("9:59", "10:00"));
        Assert.Equal([0, 1], RollingDigits.ChangedIndices("9", "10"));
    }

    [Fact]
    public void FormatClock()
    {
        Assert.Equal("4:59", RollingDigits.FormatClock(TimeSpan.FromSeconds(299)));
        Assert.Equal("1:00:05", RollingDigits.FormatClock(TimeSpan.FromSeconds(3605)));
        Assert.Equal("0:00", RollingDigits.FormatClock(TimeSpan.FromSeconds(-3)));
    }
}
