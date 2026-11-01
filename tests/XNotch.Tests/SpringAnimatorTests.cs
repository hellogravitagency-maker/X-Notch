using System;
using XNotch.Helpers;
using XNotch.Models;
using Xunit;

namespace XNotch.Tests;

public class SpringAnimatorTests
{
    [Fact]
    public void AnimateTo_SettlesAtTargetValue()
    {
        var spring = new SpringAnimator(0.40, 0.82);
        spring.AnimateTo(target: 100.0, currentValue: 0.0);

        Assert.True(spring.IsAnimating);

        // Step through frames deterministically
        for (double t = 0.01; t <= 2.0; t += 0.016)
        {
            spring.Tick(simulatedTimeSeconds: t);
            if (!spring.IsAnimating) break;
        }

        Assert.False(spring.IsAnimating);
        Assert.Equal(100.0, spring.CurrentValue, 1);
        Assert.Equal(0.0, spring.CurrentVelocity, 1);
    }

    [Fact]
    public void AnimateTo_PreservesVelocityWhenRedirected()
    {
        var spring = new SpringAnimator(0.40, 0.82);
        spring.AnimateTo(target: 100.0, currentValue: 0.0);

        // Advance 0.1s where the spring has high positive velocity
        spring.Tick(simulatedTimeSeconds: 0.1);
        double ongoingVelocity = spring.CurrentVelocity;
        Assert.True(ongoingVelocity > 50.0, $"Expected positive velocity, got {ongoingVelocity}");

        // Redirect mid-flight to target: 0.0 without passing explicit velocity
        spring.AnimateTo(target: 0.0, currentValue: spring.CurrentValue);

        // Check that initial velocity of new motion inherited ongoing positive velocity
        Assert.Equal(ongoingVelocity, spring.CurrentVelocity, precision: 2);
    }

    [Fact]
    public void CriticallyDamped_SettlesWithoutOvershoot()
    {
        var spring = new SpringAnimator(0.40, 1.0); // Critically damped
        spring.AnimateTo(target: 100.0, currentValue: 0.0);

        double maxValue = 0;
        for (double t = 0.01; t <= 2.0; t += 0.016)
        {
            double val = spring.Tick(simulatedTimeSeconds: t);
            if (val > maxValue) maxValue = val;
            if (!spring.IsAnimating) break;
        }

        Assert.False(spring.IsAnimating);
        Assert.True(maxValue <= 100.25, $"Critically damped spring overshot: {maxValue}");
    }

    [Fact]
    public void NotchDimensions_MaintainConsistentProportions()
    {
        // Ensure closed pill is compact and within natural desktop notch bounds
        Assert.True(NotchConstants.ClosedWidth <= 220);
        Assert.True(NotchConstants.ClosedHeight <= 36);

        // Audio peek capsule should be wider than standard peek to accommodate micro-controls
        Assert.True(NotchConstants.AudioPeekWidth > NotchConstants.PeekWidth);
        Assert.True(NotchConstants.AudioPeekHeight >= NotchConstants.PeekHeight);

        // Shelf drop target should have comfortable hit-test dimensions
        Assert.True(NotchConstants.ShelfDropWidth >= 300);
        Assert.True(NotchConstants.ShelfDropHeight >= 80);

        // Open state must fit desktop command view
        Assert.True(NotchConstants.OpenWidth > NotchConstants.AudioPeekWidth);
    }

    [Fact]
    public void RapidInterruption_MaintainsSmoothTrajectory()
    {
        var spring = new SpringAnimator(NotchConstants.PeekSpringResponse, NotchConstants.PeekSpringDamping);
        
        // Start expanding towards peek
        spring.AnimateTo(NotchConstants.AudioPeekWidth, NotchConstants.ClosedWidth);
        spring.Tick(simulatedTimeSeconds: 0.05);

        // Sudden click: redirect to open width mid-expansion
        spring.AnimateTo(NotchConstants.OpenWidth, spring.CurrentValue);
        Assert.True(spring.IsAnimating);

        // Advance through frames
        for (double t = 0.01; t <= 2.0; t += 0.016)
        {
            spring.Tick(simulatedTimeSeconds: t);
            if (!spring.IsAnimating) break;
        }

        Assert.False(spring.IsAnimating);
        Assert.Equal(NotchConstants.OpenWidth, spring.CurrentValue, 1);
    }
}
