using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using FlowDirection = System.Windows.FlowDirection;
using ActivitySource = XNotch.Island.ActivitySource;

namespace XNotch.Island.Activities;

/// <summary>Shared island view parts: text styles, glyphs, art, rings, waveform.</summary>
public static class IslandVisuals
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    public static readonly FontFamily Font = new(IslandMetrics.FontFamily);
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Frozen brush from "#RRGGBB" / "#AARRGGBB".</summary>
    public static SolidColorBrush Brush(string hex)
    {
        if (Brushes.TryGetValue(hex, out var b)) return b;
        b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return Brushes[hex] = b;
    }

    public static SolidColorBrush Primary => Brush(IslandColors.TextPrimary);
    public static SolidColorBrush Secondary => Brush("#8CFFFFFF"); // white at 55 %

    /// <summary>Island text: compact 13/SemiBold, title 15/SemiBold, subtitle 13/Regular (55 %).</summary>
    public static TextBlock Text(string text, double size = IslandMetrics.CompactFontSize, bool bold = true, bool secondary = false) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = secondary ? Secondary : Primary,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Icon text: Segoe Fluent glyphs (private-use code points) or plain text / emoji.</summary>
    public static TextBlock Glyph(string glyph, double size = 14, string? hex = null) => new()
    {
        Text = glyph,
        FontFamily = glyph.Length > 0 && glyph[0] >= '' && glyph[0] <= '' ? IconFont : Font,
        FontSize = size,
        Foreground = hex == null ? Primary : Brush(hex),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>Rounded album art (concentric radius).</summary>
    public static Border Art(double size, double radius, ImageSource? source) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(radius),
        Background = source == null ? Brush("#33FFFFFF") : new ImageBrush(source) { Stretch = Stretch.UniformToFill },
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// Compact pill row: leading slot at inset 6, trailing at inset 10, a centre gap
    /// between them. Its desired width drives the compact island width (240–340).
    /// </summary>
    public static FrameworkElement CompactRow(FrameworkElement? leading, FrameworkElement? trailing)
    {
        var g = new Grid { Height = IslandMetrics.CompactHeight };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = IslandMetrics.CompactCenterGap });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (leading != null)
        {
            var slot = new Grid { MinWidth = IslandMetrics.LeadingSlot, Height = IslandMetrics.LeadingSlot, Margin = new Thickness(IslandMetrics.LeadingInset, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            slot.Children.Add(leading);
            g.Children.Add(slot);
        }
        if (trailing != null)
        {
            trailing.Margin = new Thickness(0, 0, IslandMetrics.TrailingInset, 0);
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            g.Children.Add(trailing);
        }
        return g;
    }

    /// <summary>44 px circular button tinted with an accent (25 % fill), scales on press.</summary>
    public static Border CircleButton(string glyph, string hex, Action onClick, double size = 44)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B)),
            Child = Glyph(glyph, 16, hex),
            Cursor = System.Windows.Input.Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Margin = new Thickness(6, 0, 0, 0),
        };
        b.MouseLeftButtonDown += (_, e) => { ((ScaleTransform)b.RenderTransform).ScaleX = ((ScaleTransform)b.RenderTransform).ScaleY = 0.96; e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) => { ((ScaleTransform)b.RenderTransform).ScaleX = ((ScaleTransform)b.RenderTransform).ScaleY = 1; onClick(); e.Handled = true; };
        b.MouseLeave += (_, _) => ((ScaleTransform)b.RenderTransform).ScaleX = ((ScaleTransform)b.RenderTransform).ScaleY = 1;
        return b;
    }

    /// <summary>Alert / HUD row: 32 px icon, title + subtitle, optional trailing.</summary>
    public static FrameworkElement AlertRow(FrameworkElement icon, string title, string? subtitle, FrameworkElement? trailing)
    {
        var g = new Grid { Margin = new Thickness(IslandMetrics.AlertPadding, 0, IslandMetrics.AlertPadding, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Grid { Width = IslandMetrics.AlertIcon, Height = IslandMetrics.AlertIcon, VerticalAlignment = VerticalAlignment.Center };
        iconHost.Children.Add(icon);
        g.Children.Add(iconHost);

        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, IslandMetrics.TitleFontSize));
        if (!string.IsNullOrWhiteSpace(subtitle))
            text.Children.Add(Text(subtitle, IslandMetrics.SubtitleFontSize, bold: false, secondary: true));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        if (trailing != null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            g.Children.Add(trailing);
        }
        return g;
    }
}

/// <summary>Circular progress ring (track at 25 % of the colour).</summary>
public sealed class ProgressRing : FrameworkElement
{
    private double _progress;
    private readonly Pen _track, _arc;

    public ProgressRing(double size, string hex, double stroke = IslandMetrics.RingStroke)
    {
        Width = Height = size;
        var c = (Color)ColorConverter.ConvertFromString(hex);
        _arc = new Pen(new SolidColorBrush(c), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        _track = new Pen(new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B)), stroke);
        _arc.Freeze(); _track.Freeze();
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>0..1, drawn clockwise from 12 o'clock.</summary>
    public double Progress
    {
        get => _progress;
        set { value = Math.Clamp(value, 0, 1); if (Math.Abs(value - _progress) < 0.0005) return; _progress = value; InvalidateVisual(); }
    }

    /// <summary>Short scale pulse (e.g. timer done).</summary>
    public void Pulse()
    {
        if (Helpers.SpringAnimator.Reduced || RenderTransform is not ScaleTransform s) return;
        var a = new DoubleAnimation(1.25, 1.0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 6 } };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double r = (Math.Min(ActualWidth, ActualHeight) - _arc.Thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, _track, c, r, r);
        if (_progress <= 0.001) return;
        if (_progress >= 0.999) { dc.DrawEllipse(null, _arc, c, r, r); return; }
        double a = _progress * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, _progress > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, _arc, g);
    }
}

/// <summary>Four rounded bars (3 px, radius 1.5, 4–16 px tall) fed from spectrum data.</summary>
public sealed class WaveformBars : FrameworkElement
{
    private readonly double[] _heights = new double[IslandMetrics.WaveBars];
    private SolidColorBrush _brush = new(Colors.White);

    public WaveformBars()
    {
        Width = IslandMetrics.WaveBars * IslandMetrics.WaveBarWidth + (IslandMetrics.WaveBars - 1) * IslandMetrics.WaveBarGap;
        Height = IslandMetrics.WaveBarMax;
        for (int i = 0; i < _heights.Length; i++) _heights[i] = IslandMetrics.WaveBarMin;
        IsHitTestVisible = false;
    }

    public Color Tint
    {
        set { if (_brush.Color != value) { _brush = new SolidColorBrush(value); _brush.Freeze(); InvalidateVisual(); } }
    }

    /// <summary>Downsamples the spectrum to 4 bars (band averages), smoothed toward the target.</summary>
    public void Update(float[]? spectrum, bool playing)
    {
        int n = _heights.Length;
        for (int i = 0; i < n; i++)
        {
            double level = 0;
            if (playing && spectrum is { Length: > 0 })
            {
                int from = i * spectrum.Length / n, to = Math.Max(from + 1, (i + 1) * spectrum.Length / n);
                for (int k = from; k < to; k++) level += spectrum[k];
                level /= to - from;
            }
            double target = IslandMetrics.WaveBarMin + (IslandMetrics.WaveBarMax - IslandMetrics.WaveBarMin) * Math.Clamp(level, 0, 1);
            _heights[i] += (target - _heights[i]) * 0.45;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double x = 0;
        foreach (var h in _heights)
        {
            dc.DrawRoundedRectangle(_brush, null,
                new Rect(x, (ActualHeight - h) / 2, IslandMetrics.WaveBarWidth, h),
                IslandMetrics.WaveBarRadius, IslandMetrics.WaveBarRadius);
            x += IslandMetrics.WaveBarWidth + IslandMetrics.WaveBarGap;
        }
    }
}
