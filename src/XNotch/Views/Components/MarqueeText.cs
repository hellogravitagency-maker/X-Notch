using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace XNotch.Views.Components;

/// <summary>
/// A TextBlock that scrolls horizontally when the text is wider than the container.
/// Matches the boring.notch MarqueeText behavior.
/// </summary>
public class MarqueeText : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeText),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty SpeedProperty =
        DependencyProperty.Register(nameof(Speed), typeof(double), typeof(MarqueeText),
            new PropertyMetadata(30.0));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    private TextBlock? _textBlock;
    private Canvas? _canvas;
    private Storyboard? _storyboard;
    private double _lastTextWidth = -1;
    private double _lastContainerWidth = -1;

    static MarqueeText()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MarqueeText),
            new FrameworkPropertyMetadata(typeof(MarqueeText)));
    }

    public MarqueeText()
    {
        // Subscribe once here — OnApplyTemplate can run more than once
        SizeChanged += (_, _) => UpdateAnimation();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _canvas = GetTemplateChild("PART_Canvas") as Canvas;
        _textBlock = GetTemplateChild("PART_Text") as TextBlock;

        if (_textBlock != null)
        {
            _textBlock.Text = Text;
        }

        _lastTextWidth = -1;
        UpdateAnimation();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeText marquee)
        {
            if (marquee._textBlock != null)
                marquee._textBlock.Text = (string)e.NewValue;
            marquee.UpdateAnimation();
        }
    }

    private void UpdateAnimation()
    {
        if (_textBlock == null || _canvas == null) return;

        _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = _textBlock.DesiredSize.Width;
        double containerWidth = ActualWidth;

        // The container resizes every frame while the notch spring-animates;
        // restarting the storyboard each frame would freeze the marquee.
        if (Math.Abs(textWidth - _lastTextWidth) < 0.5 &&
            Math.Abs(containerWidth - _lastContainerWidth) < 0.5)
            return;
        _lastTextWidth = textWidth;
        _lastContainerWidth = containerWidth;

        _storyboard?.Stop();
        _storyboard = null;

        if (textWidth <= containerWidth)
        {
            // Text fits — no scrolling
            var tt = _textBlock.RenderTransform as TranslateTransform ?? new TranslateTransform();
            _textBlock.RenderTransform = tt;
            tt.X = 0;
            return;
        }

        // Scroll: start at 0, scroll left by (textWidth - containerWidth), pause, then reset
        double scrollDistance = textWidth - containerWidth;
        double scrollDuration = scrollDistance / Speed;

        var translateTransform = new TranslateTransform(0, 0);
        _textBlock.RenderTransform = translateTransform;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        // Hold at start for 2 seconds
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));

        // Scroll left
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + scrollDuration))));

        // Hold at end for 2 seconds
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + scrollDuration))));

        // Jump back (via another cycle)

        _storyboard = new Storyboard();
        Storyboard.SetTarget(animation, _textBlock);
        Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.X"));
        _storyboard.Children.Add(animation);
        _storyboard.Begin();
    }
}
