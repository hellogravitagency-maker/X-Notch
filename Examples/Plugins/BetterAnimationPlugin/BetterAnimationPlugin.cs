using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XNotch.Plugins;

namespace BetterAnimationPlugin;

/// <summary>
/// Example plugin that replaces the default notch animations with smooth fade + slide effects.
/// </summary>
[XNotchPlugin("com.xnotch.betteranimation", "Better Animation", "1.0.0", "X-Notch Team")]
public class BetterAnimationPlugin : PluginBase, IAnimationPlugin
{
    public override string Id => "com.xnotch.betteranimation";
    public override string Name => "Better Animation";
    public override string Version => "1.0.0";
    public override string Author => "X-Notch Team";
    public override string Description => "Replaces default animations with smooth fade + slide effects";

    public double AnimationDurationMs => 450;
    public bool ReplaceDefaultAnimations => true;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);
        context.Log("Better Animation plugin loaded - smooth animations enabled!");
    }

    public Storyboard? CreateExpandAnimation(FrameworkElement target, double fromWidth, double toWidth, double fromHeight, double toHeight)
    {
        var storyboard = new Storyboard();

        // Width animation with elastic easing
        var widthAnim = new DoubleAnimation
        {
            From = fromWidth,
            To = toWidth,
            Duration = TimeSpan.FromMilliseconds(AnimationDurationMs),
            EasingFunction = new ElasticEase
            {
                Oscillations = 1,
                Springiness = 4,
                EasingMode = EasingMode.EaseOut
            }
        };
        Storyboard.SetTarget(widthAnim, target);
        Storyboard.SetTargetProperty(widthAnim, new PropertyPath("Width"));
        storyboard.Children.Add(widthAnim);

        // Height animation with elastic easing
        var heightAnim = new DoubleAnimation
        {
            From = fromHeight,
            To = toHeight,
            Duration = TimeSpan.FromMilliseconds(AnimationDurationMs),
            EasingFunction = new ElasticEase
            {
                Oscillations = 1,
                Springiness = 4,
                EasingMode = EasingMode.EaseOut
            }
        };
        Storyboard.SetTarget(heightAnim, target);
        Storyboard.SetTargetProperty(heightAnim, new PropertyPath("Height"));
        storyboard.Children.Add(heightAnim);

        // Fade in animation
        var opacityAnim = new DoubleAnimation
        {
            From = 0.7,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, target);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));
        storyboard.Children.Add(opacityAnim);

        // Scale animation for a "pop" effect
        var scaleTransform = new ScaleTransform(0.95, 0.95, toWidth / 2, toHeight / 2);
        target.RenderTransform = scaleTransform;

        var scaleXAnim = new DoubleAnimation
        {
            From = 0.95,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(AnimationDurationMs),
            EasingFunction = new BackEase
            {
                Amplitude = 0.3,
                EasingMode = EasingMode.EaseOut
            }
        };
        Storyboard.SetTarget(scaleXAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("ScaleX"));
        storyboard.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimation
        {
            From = 0.95,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(AnimationDurationMs),
            EasingFunction = new BackEase
            {
                Amplitude = 0.3,
                EasingMode = EasingMode.EaseOut
            }
        };
        Storyboard.SetTarget(scaleYAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("ScaleY"));
        storyboard.Children.Add(scaleYAnim);

        return storyboard;
    }

    public Storyboard? CreateCollapseAnimation(FrameworkElement target, double fromWidth, double toWidth, double fromHeight, double toHeight)
    {
        var storyboard = new Storyboard();

        // Width animation
        var widthAnim = new DoubleAnimation
        {
            From = fromWidth,
            To = toWidth,
            Duration = TimeSpan.FromMilliseconds(AnimationDurationMs * 0.8),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(widthAnim, target);
        Storyboard.SetTargetProperty(widthAnim, new PropertyPath("Width"));
        storyboard.Children.Add(widthAnim);

        // Height animation
        var heightAnim = new DoubleAnimation
        {
            From = fromHeight,
            To = toHeight,
            Duration = TimeSpan.FromMilliseconds(AnimationDurationMs * 0.8),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(heightAnim, target);
        Storyboard.SetTargetProperty(heightAnim, new PropertyPath("Height"));
        storyboard.Children.Add(heightAnim);

        // Fade out slightly
        var opacityAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.9,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(opacityAnim, target);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));
        storyboard.Children.Add(opacityAnim);

        return storyboard;
    }

    public Storyboard? CreatePeekAnimation(FrameworkElement target)
    {
        var storyboard = new Storyboard();

        // Gentle scale pulse
        var scaleTransform = target.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        target.RenderTransform = scaleTransform;

        var scaleAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 1.05,
            Duration = TimeSpan.FromMilliseconds(200),
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(scaleAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleAnim, new PropertyPath("ScaleX"));
        storyboard.Children.Add(scaleAnim);

        var scaleYAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 1.05,
            Duration = TimeSpan.FromMilliseconds(200),
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(scaleYAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("ScaleY"));
        storyboard.Children.Add(scaleYAnim);

        return storyboard;
    }

    public IEasingFunction? GetCustomEasingFunction()
    {
        // Return a custom easing function for general use
        return new QuarticEase { EasingMode = EasingMode.EaseOut };
    }
}
