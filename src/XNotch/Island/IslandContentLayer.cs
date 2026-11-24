using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using XNotch.Helpers;

namespace XNotch.Island;

/// <summary>
/// Hosts the island's compact / minimal / alert / hud content above the black shape
/// (content never distorts with the shape). Swapping content crossfades: the old view
/// exits first (opacity → 0, blur → 6, 120 ms ease-in) while the new one enters after
/// <see cref="IslandMetrics.ContentEnterDelayMs"/> on <see cref="IslandSprings.ContentIn"/>
/// (opacity 0 → 1, scale 0.92 → 1, blur 8 → 0). Effects are dropped once settled.
/// Driven by the window's frame loop through <see cref="Tick"/>.
/// </summary>
public sealed class IslandContentLayer : Grid
{
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

    private readonly SpringAnimator _enter = new SpringAnimator().Use(IslandSprings.ContentIn);
    private readonly List<(FrameworkElement Element, long Start, double From)> _exiting = [];
    private FrameworkElement? _current;
    private long _enterAt;
    private bool _pendingEnter;

    /// <summary>Raised when the layer needs frames (the window subscribes its loop).</summary>
    public event Action? FramesNeeded;

    /// <summary>Identity of what is shown (e.g. "compact:media"); same key = no swap.</summary>
    public string? ContentKey { get; private set; }

    /// <summary>The element currently shown (entering or settled).</summary>
    public FrameworkElement? Current => _current;

    public IslandContentLayer()
    {
        ClipToBounds = false;
        IsHitTestVisible = true;
    }

    /// <summary>
    /// Shows the view for <paramref name="key"/>, building it with <paramref name="factory"/>
    /// only when the key changes. <paramref name="enterDelayMs"/> lets the shape lead.
    /// </summary>
    public void Show(string? key, Func<FrameworkElement?> factory, int enterDelayMs = IslandMetrics.ContentEnterDelayMs)
    {
        if (key == ContentKey) return;
        ContentKey = key;

        if (_current != null) BeginExit(_current);
        _current = key == null ? null : factory();
        if (_current != null)
        {
            _current.Opacity = 0;
            _current.IsHitTestVisible = false;
            _current.RenderTransformOrigin = new Point(0.5, 0.5);
            _current.RenderTransform = new ScaleTransform(Reduced ? 1 : IslandMetrics.ContentEnterScale, Reduced ? 1 : IslandMetrics.ContentEnterScale);
            if (!Reduced) _current.Effect = new BlurEffect { Radius = IslandMetrics.ContentEnterBlur, RenderingBias = RenderingBias.Performance };
            Children.Add(_current);
            _enter.Reset(0);
            _pendingEnter = true;
            _enterAt = Stopwatch.GetTimestamp() + (long)(Math.Max(0, enterDelayMs) * TicksPerMs);
        }
        FramesNeeded?.Invoke();
    }

    /// <summary>Hides everything (exit choreography), e.g. while Expanded.</summary>
    public void Clear() => Show(null, () => null);

    /// <summary>Advances enter/exit. Returns true while anything is moving.</summary>
    public bool Tick()
    {
        bool moving = false;
        long now = Stopwatch.GetTimestamp();

        if (_current != null && _pendingEnter && now >= _enterAt)
        {
            _pendingEnter = false;
            _enter.AnimateTo(1, 0);
        }
        if (_current != null && (_pendingEnter || _enter.IsAnimating))
        {
            moving = true;
            double p = _pendingEnter ? 0 : Reduced ? FadeProgress(now) : _enter.Tick();
            ApplyEnter(_current, p);
            if (!_pendingEnter && !_enter.IsAnimating && (!Reduced || p >= 1)) Settle(_current);
        }

        for (int i = _exiting.Count - 1; i >= 0; i--)
        {
            var (el, start, from) = _exiting[i];
            double duration = Reduced ? IslandMetrics.ReducedFadeMs : IslandMetrics.ContentExitMs;
            double t = Math.Clamp((now - start) / TicksPerMs / duration, 0, 1);
            double eased = t * t; // ease-in: the exit gets out of the way
            el.Opacity = from * (1 - eased);
            if (el.Effect is BlurEffect b) b.Radius = IslandMetrics.ContentExitBlur * eased;
            if (t >= 1) { Children.Remove(el); _exiting.RemoveAt(i); }
            else moving = true;
        }
        return moving;
    }

    private double FadeProgress(long now)
    {
        double t = (now - _enterAt) / TicksPerMs / IslandMetrics.ReducedFadeMs;
        return Math.Clamp(t, 0, 1);
    }

    private static void ApplyEnter(FrameworkElement el, double p)
    {
        el.Opacity = Math.Clamp(p, 0, 1);
        if (el.RenderTransform is ScaleTransform s)
        {
            double scale = IslandMetrics.ContentEnterScale + (1 - IslandMetrics.ContentEnterScale) * p;
            s.ScaleX = s.ScaleY = Reduced ? 1 : scale;
        }
        if (el.Effect is BlurEffect b) b.Radius = Math.Max(0, IslandMetrics.ContentEnterBlur * (1 - p));
        el.IsHitTestVisible = p > 0.5;
    }

    private static void Settle(FrameworkElement el)
    {
        el.Opacity = 1;
        el.Effect = null; // no offscreen blur pass while idle
        el.RenderTransform = Transform.Identity;
        el.IsHitTestVisible = true;
    }

    private void BeginExit(FrameworkElement el)
    {
        el.IsHitTestVisible = false;
        if (!Reduced && el.Effect is not BlurEffect)
            el.Effect = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
        _exiting.Add((el, Stopwatch.GetTimestamp(), el.Opacity));
    }

    private static bool Reduced => SpringAnimator.Reduced;
}
