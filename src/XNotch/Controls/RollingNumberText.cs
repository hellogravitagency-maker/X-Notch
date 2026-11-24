using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using XNotch.Helpers;
using XNotch.Island;

using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using FlowDirection = System.Windows.FlowDirection;
using ActivitySource = XNotch.Island.ActivitySource;

namespace XNotch.Controls;

/// <summary>
/// Text whose characters roll vertically when they change: the old character slides
/// up and out while the new one slides in from below (DigitRoll spring). Only changed
/// columns animate, digit columns share one tabular width so the text never jitters.
/// Rendered directly (no child elements); frames are requested only while rolling.
/// </summary>
public sealed class RollingNumberText : FrameworkElement
{
    private sealed class Column
    {
        public char Old;
        public char New;
        public readonly SpringAnimator Spring = new SpringAnimator().Use(IslandSprings.DigitRoll);
        public double Progress = 1;
    }

    private readonly List<Column> _columns = [];
    private string _text = "";
    private bool _hooked;
    private Typeface _typeface;
    private double _fontSize;
    private Brush _foreground;
    private double _digitWidth, _lineHeight;

    public RollingNumberText(double fontSize = IslandMetrics.NumberFontSize, FontWeight? weight = null, Brush? foreground = null)
    {
        _fontSize = fontSize;
        _foreground = foreground ?? Brushes.White;
        _typeface = new Typeface(new FontFamily(IslandMetrics.FontFamily), FontStyles.Normal, weight ?? FontWeights.SemiBold, FontStretches.Normal);
        IsHitTestVisible = false;
        Unloaded += (_, _) => Unhook();
    }

    /// <summary>The displayed text. Changed characters roll; equal ones stay put.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text) return;
            var changed = new HashSet<int>(RollingDigits.ChangedIndices(_text, value));
            var previous = _text;
            int offset = value.Length - previous.Length;
            bool reduced = SpringAnimator.Reduced || !IsLoaded;

            _columns.Clear();
            for (int i = 0; i < value.Length; i++)
            {
                int j = i - offset;
                var col = new Column { New = value[i], Old = j >= 0 && j < previous.Length ? previous[j] : ' ' };
                if (changed.Contains(i) && !reduced)
                {
                    col.Progress = 0;
                    col.Spring.AnimateTo(1, 0);
                }
                _columns.Add(col);
            }
            bool widthChanged = value.Length != previous.Length;
            _text = value;
            if (widthChanged) InvalidateMeasure();
            InvalidateVisual();
            if (_columns.Exists(c => c.Progress < 1)) Hook();
        }
    }

    public Brush Foreground
    {
        get => _foreground;
        set { _foreground = value; InvalidateVisual(); }
    }

    private double Ppd => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private FormattedText Format(char c) =>
        new(c.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, _fontSize, _foreground, Ppd);

    private double ColumnWidth(char c) => char.IsDigit(c) ? _digitWidth : Format(c).WidthIncludingTrailingWhitespace;

    private void EnsureMetrics()
    {
        if (_digitWidth > 0) return;
        for (char d = '0'; d <= '9'; d++)
        {
            var f = Format(d);
            _digitWidth = Math.Max(_digitWidth, f.WidthIncludingTrailingWhitespace);
            _lineHeight = Math.Max(_lineHeight, f.Height);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();
        double w = 0;
        foreach (var c in _text) w += ColumnWidth(c);
        return new Size(w, _lineHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMetrics();
        double x = 0, h = _lineHeight;
        foreach (var col in _columns)
        {
            double w = ColumnWidth(col.New);
            if (col.Progress >= 1)
            {
                DrawCentered(dc, col.New, x, 0, w, 1);
            }
            else
            {
                dc.PushClip(new RectangleGeometry(new Rect(x, 0, w, h)));
                double p = Math.Clamp(col.Progress, 0, 1.2);
                if (col.Old != ' ') DrawCentered(dc, col.Old, x, -p * h, w, Math.Clamp(1 - p, 0, 1));
                DrawCentered(dc, col.New, x, (1 - p) * h, w, Math.Clamp(p, 0, 1));
                dc.Pop();
            }
            x += w;
        }
    }

    private void DrawCentered(DrawingContext dc, char c, double x, double y, double w, double opacity)
    {
        if (opacity <= 0.001) return;
        var f = Format(c);
        dc.PushOpacity(opacity);
        dc.DrawText(f, new Point(x + (w - f.WidthIncludingTrailingWhitespace) / 2, y));
        dc.Pop();
    }

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        bool moving = false;
        foreach (var col in _columns)
        {
            if (!col.Spring.IsAnimating) { col.Progress = 1; continue; }
            col.Progress = col.Spring.Tick();
            moving = true;
        }
        InvalidateVisual();
        if (!moving) Unhook();
    }
}
