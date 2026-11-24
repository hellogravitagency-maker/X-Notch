using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using XNotch.Helpers;
using XNotch.Island;
using XNotch.Models;

namespace XNotch.Views;

/// <summary>
/// Dynamic Island presentation layer (top dock, Dynamic mode). Classic mode never
/// enters any of these paths, so it renders exactly as v0.9.0.
/// Choreography: on expand the shape morphs first and content follows 90 ms later;
/// on collapse content exits first (120 ms) and then the shape shrinks, ending
/// with a short squash. Every shape change retargets from the live value and velocity.
/// </summary>
public partial class NotchWindow
{
    private static readonly double IslandTicksPerMs = Stopwatch.Frequency / 1000.0;

    private IslandPresentationMachine? _island;
    private IslandContentLayer? _islandLayer;
    private Path? _islandShadowPath;
    private IslandPresentation _lastPresentation = IslandPresentation.Idle;
    private string? _lastPrimaryId, _lastSecondaryId;
    private bool _lastPeeking;
    private bool _islandShadowActive;
    private bool _renderingHooked;

    // Expanded content choreography
    private DispatcherTimer? _contentEnterTimer;
    private bool _exitFadeActive;
    private long _exitFadeStart;
    private double _exitFadeFrom;

    // Collapse squash
    private bool _pendingSquash;
    private bool _squashHold;
    private long _squashStart;
    private readonly SpringAnimator _squashSpring = new SpringAnimator().Use(IslandSprings.SquashRecover);
    private readonly ScaleTransform _squashTransform = new();

    // Minimal goo: 0 = circle merged into the pill's end cap, 1 = detached at the 8 px gap
    private readonly SpringAnimator _gooSpring = new SpringAnimator().Use(IslandSprings.GooSplit);
    private double _goo;

    // Alert / HUD expiry
    private DispatcherTimer? _islandTimer;
    private IslandMode _appliedIslandMode;
    private IslandStyle _appliedIslandStyle;

    private bool IsDynamic => _settings.IslandMode == IslandMode.Dynamic;
    private bool IsDynamicTop => IsDynamic && _dock == NotchDock.Top && !_isDragging;
    private bool IsFloating => _settings.IslandStyle == IslandStyle.Floating;

    /// <summary>The presentation machine (null until loaded).</summary>
    internal IslandPresentationMachine? Island => _island;

    #region Frame loop (on demand)

    private void HookRendering()
    {
        if (_renderingHooked) return;
        _renderingHooked = true;
        System.Windows.Media.CompositionTarget.Rendering += OnRendering;
    }

    private void UnhookRendering()
    {
        if (!_renderingHooked) return;
        _renderingHooked = false;
        System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
    }

    private void OnAnySpringStarted()
    {
        if (Dispatcher.CheckAccess()) HookRendering();
        else Dispatcher.BeginInvoke(HookRendering);
    }

    #endregion

    private void InitIsland()
    {
        _island = new IslandPresentationMachine(SystemClock.Instance);
        _island.Changed += () => RetargetIsland();

        _islandLayer = new IslandContentLayer { Visibility = Visibility.Collapsed };
        _islandLayer.FramesNeeded += HookRendering;
        ContentGrid.Children.Add(_islandLayer);

        _islandShadowPath = new Path
        {
            Fill = Brushes.Black,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = new BlurEffect { Radius = IslandMetrics.ShadowBlur, RenderingBias = RenderingBias.Performance },
        };
        NotchCanvas.Children.Insert(0, _islandShadowPath);

        _islandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IslandMetrics.AlertTickMs) };
        _islandTimer.Tick += (_, _) =>
        {
            _island.Update();
            if (!_island.Alerts.IsBusy && !_island.HudActive) _islandTimer.Stop();
        };
        _island.Changed += () =>
        {
            if (_island.Alerts.IsBusy || _island.HudActive) _islandTimer.Start();
        };

        _appliedIslandMode = _settings.IslandMode;
        _appliedIslandStyle = _settings.IslandStyle;
        RefreshIslandActivities();
        ApplyIslandMode(snap: true);
    }

    /// <summary>Queues a transient alert (Dynamic mode).</summary>
    private void PostIslandAlert(IslandAlert alert) => _island?.PostAlert(alert);

    /// <summary>Switches between Dynamic and Classic visuals (startup, settings change, dock change).</summary>
    private void ApplyIslandMode(bool snap = false)
    {
        if (_island == null || _islandLayer == null) return;
        bool on = IsDynamicTop;

        NotchPath.SetResourceReference(Shape.FillProperty, on ? "IslandFillBrush" : "NotchFillBrush");
        OpenContent.Margin = on ? new Thickness(IslandMetrics.ExpandedPadding) : new Thickness(16, 8, 16, 12);
        _islandLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        NotchCanvas.RenderTransform = on ? _squashTransform : null;
        if (_islandShadowPath != null && !on) _islandShadowPath.Visibility = Visibility.Collapsed;
        if (AmbientAuraPath.Effect is BlurEffect aura) aura.Radius = on ? IslandMetrics.GlowBlur : 24;

        if (on)
        {
            _island.SetInteraction(_vm.NotchState);
            _lastPresentation = (IslandPresentation)(-1); // force a full retarget
            if (snap)
            {
                var (w, h, r) = IslandSize(_island.Presentation);
                _currentWidth = w; _currentHeight = h; _currentBottomRadius = r;
                _currentTopRadius = IslandTopRadius(_island.Presentation);
                _widthSpring.Reset(w); _heightSpring.Reset(h);
                _bottomRadiusSpring.Reset(r); _topRadiusSpring.Reset(_currentTopRadius);
            }
            RetargetIsland();
        }
        else
        {
            _islandLayer.Clear();
            _exitFadeActive = false;
            _pendingSquash = _squashHold = false;
            _squashTransform.ScaleX = _squashTransform.ScaleY = 1;
            OpenContent.Effect = null;
            if (!snap) RestoreClassicTargets();
        }
        UpdateNotchVisuals();
    }

    private void RestoreClassicTargets()
    {
        bool open = _vm.NotchState == NotchState.Open;
        bool top = _dock == NotchDock.Top;
        double w = open ? (top ? GetOpenWidth() : NotchConstants.SideOpenWidth) : (top ? NotchConstants.ClosedWidth : NotchConstants.ClosedHeight);
        double h = open ? (top ? GetTopOpenHeight() : GetSideOpenHeight()) : (top ? NotchConstants.ClosedHeight : NotchConstants.ClosedWidth);
        Aim(_widthSpring.Use(IslandSprings.Collapse), w, _currentWidth);
        Aim(_heightSpring.Use(IslandSprings.Collapse), h, _currentHeight);
        Aim(_topRadiusSpring, open ? NotchConstants.OpenTopRadius : NotchConstants.ClosedTopRadius, _currentTopRadius);
        Aim(_bottomRadiusSpring, open ? NotchConstants.OpenBottomRadius : NotchConstants.ClosedBottomRadius, _currentBottomRadius);
    }

    /// <summary>Starts a spring from the window's live value (springs at rest don't know it).</summary>
    private static void Aim(SpringAnimator s, double target, double current)
    {
        if (s.IsAnimating) s.Retarget(target);
        else if (Math.Abs(current - target) > 0.01) s.AnimateTo(target, current, 0);
    }

    #region Targets

    private (double W, double H, double R) IslandSize(IslandPresentation p)
    {
        var (w, h, r) = p switch
        {
            IslandPresentation.Compact => (CompactWidth(), IslandMetrics.CompactHeight, IslandMetrics.Capsule(IslandMetrics.CompactHeight)),
            IslandPresentation.Minimal => (IslandMetrics.MinimalPillWidth, IslandMetrics.CompactHeight, IslandMetrics.Capsule(IslandMetrics.CompactHeight)),
            IslandPresentation.Expanded => (GetOpenWidth(), DynamicOpenHeight(), IslandMetrics.ExpandedRadius),
            IslandPresentation.Alert or IslandPresentation.Hud => (IslandMetrics.AlertWidth, IslandMetrics.AlertHeight, IslandMetrics.AlertRadius),
            IslandPresentation.Spotlight => (IslandMetrics.SpotlightWidth,
                _spotlightHasResults ? IslandMetrics.SpotlightResultsHeight : IslandMetrics.SpotlightHeight,
                IslandMetrics.SpotlightRadius),
            _ => (IslandMetrics.IdleWidth, IslandMetrics.IdleHeight, IslandMetrics.Capsule(IslandMetrics.IdleHeight)),
        };
        if (_island?.IsPeeking == true)
        {
            w += IslandMetrics.PeekGrowWidth;
            h += IslandMetrics.PeekGrowHeight;
            r = IslandMetrics.Capsule(h);
        }
        return (w, h, r);
    }

    /// <summary>Attached style keeps the classic ears; Floating is a free pill.</summary>
    private double IslandTopRadius(IslandPresentation p) =>
        IsFloating ? 0 : p == IslandPresentation.Expanded ? NotchConstants.OpenTopRadius : NotchConstants.ClosedTopRadius;

    /// <summary>Open height with the 16 px expanded padding (the classic margin was 8 / 12).</summary>
    private double DynamicOpenHeight() => GetTopOpenHeight() + (IslandMetrics.ExpandedPadding - 8) + (IslandMetrics.ExpandedPadding - 12);

    /// <summary>Compact grows to fit its content (240–340).</summary>
    private double CompactWidth()
    {
        double w = IslandMetrics.CompactMinWidth;
        if (_islandLayer?.Current is { } c)
        {
            c.Measure(new Size(double.PositiveInfinity, IslandMetrics.CompactHeight));
            w = c.DesiredSize.Width;
        }
        return Math.Clamp(w, IslandMetrics.CompactMinWidth, IslandMetrics.CompactMaxWidth);
    }

    #endregion

    #region Retarget + choreography

    /// <summary>
    /// Re-aims the island at the machine's presentation. Idempotent and interruptible:
    /// every call retargets from the live value + velocity.
    /// </summary>
    private void RetargetIsland(bool force = false)
    {
        if (_island == null || !IsDynamicTop) return;

        var p = _island.Presentation;
        var prev = _lastPresentation;
        string? primary = _island.Primary?.Id, secondary = _island.Secondary?.Id;
        bool presentationChanged = p != prev;
        bool contentChanged = presentationChanged || primary != _lastPrimaryId || secondary != _lastSecondaryId;
        bool peekChanged = _island.IsPeeking != _lastPeeking;
        if (!presentationChanged && !contentChanged && !peekChanged && !force) return; // keep the running preset
        _lastPresentation = p; _lastPrimaryId = primary; _lastSecondaryId = secondary; _lastPeeking = _island.IsPeeking;

        // Leaving Expanded: content out first, then the shape (handled when the fade ends).
        if (prev == IslandPresentation.Expanded && presentationChanged)
        {
            CancelContentEnter();
            _contentOpacitySpring.Reset(_currentContentOpacity);
            _contentScaleSpring.Reset(_currentContentScale);
            _exitFadeActive = true;
            _exitFadeStart = Stopwatch.GetTimestamp();
            _exitFadeFrom = _currentContentOpacity;
            HookRendering();
            return;
        }
        if (_exitFadeActive && p != IslandPresentation.Expanded) return; // applied when the fade ends

        if (p == IslandPresentation.Expanded)
        {
            _islandShadowActive = true;
            _exitFadeActive = false;
            _islandLayer?.Clear();
            AimShape(p, IslandSprings.ExpandActivity);
            if (presentationChanged) ScheduleContentEnter(_currentContentOpacity < 0.05 ? IslandMetrics.ContentEnterDelayMs : 0);
            return;
        }

        var preset = p switch
        {
            IslandPresentation.Alert or IslandPresentation.Hud => IslandSprings.AlertIn,
            IslandPresentation.Spotlight => IslandSprings.ExpandActivity,
            _ when presentationChanged && IslandArea(p) < _currentWidth * _currentHeight => IslandSprings.Collapse,
            _ when presentationChanged || contentChanged => IslandSprings.ExpandActivity,
            _ => IslandSprings.Peek, // hover peek / unpeek
        };
        if (contentChanged) ShowIslandContent(p);
        AimShape(p, preset);
        AimGoo(p);
    }

    private double IslandArea(IslandPresentation p)
    {
        var (w, h, _) = IslandSize(p);
        return w * h;
    }

    private void AimShape(IslandPresentation p, SpringPreset preset)
    {
        var (w, h, r) = IslandSize(p);
        Aim(_widthSpring.Use(preset), w, _currentWidth);
        Aim(_heightSpring.Use(preset), h, _currentHeight);
        Aim(_bottomRadiusSpring.Use(preset), r, _currentBottomRadius);
        Aim(_topRadiusSpring.Use(preset), IslandTopRadius(p), _currentTopRadius);
        HookRendering();
    }

    private void AimGoo(IslandPresentation p)
    {
        double target = p == IslandPresentation.Minimal ? 1 : 0;
        if (SpringAnimator.Reduced) { _gooSpring.Reset(target); _goo = target; return; }
        if (Math.Abs(_gooSpring.TargetValue - target) > 0.001 || (!_gooSpring.IsAnimating && Math.Abs(_goo - target) > 0.001))
            Aim(_gooSpring, target, _goo);
    }

    private void ScheduleContentEnter(int delayMs)
    {
        CancelContentEnter();
        if (_currentContentOpacity < 0.05)
        {
            _currentContentScale = IslandMetrics.ContentEnterScale;
            _contentScaleSpring.Reset(_currentContentScale);
        }
        void Enter()
        {
            _contentOpacitySpring.Use(IslandSprings.ContentIn);
            _contentScaleSpring.Use(IslandSprings.ContentIn);
            Aim(_contentOpacitySpring, 1.0, _currentContentOpacity);
            Aim(_contentScaleSpring, 1.0, _currentContentScale);
            if (!SpringAnimator.Reduced && OpenContent.Effect == null)
                OpenContent.Effect = new BlurEffect { Radius = IslandMetrics.ContentEnterBlur, RenderingBias = RenderingBias.Performance };
            HookRendering();
        }
        if (delayMs <= 0) { Enter(); return; }
        _contentEnterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        _contentEnterTimer.Tick += (_, _) => { CancelContentEnter(); Enter(); };
        _contentEnterTimer.Start();
    }

    private void CancelContentEnter()
    {
        _contentEnterTimer?.Stop();
        _contentEnterTimer = null;
    }

    /// <summary>Builds the compact / minimal / alert / hud view for the presentation (Phase 3+ fills these in).</summary>
    private void ShowIslandContent(IslandPresentation p)
    {
        if (_islandLayer == null || _island == null) return;
        string? key = IslandContentKey(p);
        _islandLayer.Show(key, () => BuildIslandContent(p));
    }

    private string? IslandContentKey(IslandPresentation p) => p switch
    {
        IslandPresentation.Compact => "compact:" + _island?.Primary?.Id,
        IslandPresentation.Minimal => "minimal:" + _island?.Primary?.Id + "+" + _island?.Secondary?.Id,
        IslandPresentation.Alert => "alert:" + _island?.Alerts.Current?.GetHashCode(),
        IslandPresentation.Hud => "hud",
        _ => null,
    };

    #endregion

    /// <summary>Per-frame island work. Returns true while anything island-side is moving.</summary>
    private bool TickIsland()
    {
        if (_island == null) return false;
        bool moving = _islandLayer?.Tick() == true;
        long now = Stopwatch.GetTimestamp();

        // Expanded content exit (120 ms ease-in), then the shape collapses.
        if (_exitFadeActive)
        {
            moving = true;
            double duration = SpringAnimator.Reduced ? IslandMetrics.ReducedFadeMs : IslandMetrics.ContentExitMs;
            double t = Math.Clamp((now - _exitFadeStart) / IslandTicksPerMs / duration, 0, 1);
            double eased = t * t;
            _currentContentOpacity = _exitFadeFrom * (1 - eased);
            if (!SpringAnimator.Reduced)
            {
                if (OpenContent.Effect is not BlurEffect) OpenContent.Effect = new BlurEffect { RenderingBias = RenderingBias.Performance };
                ((BlurEffect)OpenContent.Effect).Radius = IslandMetrics.ContentExitBlur * eased;
            }
            if (t >= 1)
            {
                _exitFadeActive = false;
                _currentContentOpacity = 0;
                _currentContentScale = IslandMetrics.ContentEnterScale;
                OpenContent.Effect = null;
                var p = _island.Presentation;
                if (p != IslandPresentation.Expanded)
                {
                    ShowIslandContent(p);
                    AimShape(p, p is IslandPresentation.Alert or IslandPresentation.Hud ? IslandSprings.AlertIn : IslandSprings.Collapse);
                    AimGoo(p);
                    _pendingSquash = true; // also ends the shadow; the squash itself is skipped when reduced
                }
            }
        }
        else if (OpenContent.Effect is BlurEffect enterBlur && IsDynamicTop)
        {
            // Expanded content entering: blur 8 → 0, dropped once settled.
            if (_contentOpacitySpring.IsAnimating) enterBlur.Radius = IslandMetrics.ContentEnterBlur * Math.Clamp(1 - _currentContentOpacity, 0, 1);
            else OpenContent.Effect = null;
        }

        // Squash when the collapse settles: hold 1.04 × 0.94 for 80 ms, then spring back.
        if (_pendingSquash && !_widthSpring.IsAnimating && !_heightSpring.IsAnimating)
        {
            _pendingSquash = false;
            _islandShadowActive = false;
            _squashHold = true;
            _squashStart = now;
        }
        if (_squashHold && SpringAnimator.Reduced) _squashHold = false;
        if (_squashHold)
        {
            moving = true;
            _squashTransform.ScaleX = IslandMetrics.SquashScaleX;
            _squashTransform.ScaleY = IslandMetrics.SquashScaleY;
            if ((now - _squashStart) / IslandTicksPerMs >= IslandMetrics.SquashMs)
            {
                _squashHold = false;
                _squashSpring.Reset(0);
                _squashSpring.AnimateTo(1, 0);
            }
        }
        else if (_squashSpring.IsAnimating)
        {
            moving = true;
            double v = _squashSpring.Tick();
            _squashTransform.ScaleX = IslandMetrics.SquashScaleX + (1 - IslandMetrics.SquashScaleX) * v;
            _squashTransform.ScaleY = IslandMetrics.SquashScaleY + (1 - IslandMetrics.SquashScaleY) * v;
        }
        else if (_squashTransform.ScaleX != 1 || _squashTransform.ScaleY != 1)
        {
            _squashTransform.ScaleX = _squashTransform.ScaleY = 1;
        }

        if (_gooSpring.IsAnimating)
        {
            _goo = _gooSpring.Tick();
            moving = true;
        }

        moving |= TickIslandExtras();
        return moving;
    }

    /// <summary>
    /// Island layout inside <see cref="UpdateNotchVisuals"/>: geometry (goo), radii,
    /// floating offset, shadow and squash centre. Returns the geometry to use.
    /// </summary>
    private Geometry IslandGeometry(Rect rect, double topR, double bottomR)
    {
        var pill = NotchShape.CreateNotchGeometry(rect, topR, bottomR);
        if (_goo <= 0.001 || !IsFloating) return pill;

        double r = IslandMetrics.GooRadius;
        double merged = rect.Right - r;
        double split = rect.Right + IslandMetrics.MinimalGap + r;
        var center = new Point(merged + (split - merged) * _goo, rect.Top + rect.Height / 2);
        return GooBridgeGeometry.CreateMinimal(pill, rect, center, IslandMetrics.MinimalCircle / 2);
    }

    /// <summary>Where the detached circle's centre is, in window coordinates (for its content).</summary>
    private Point MinimalCircleCenter(double offsetX, double offsetY, double w, double h)
    {
        double r = IslandMetrics.GooRadius;
        double merged = w - r, split = w + IslandMetrics.MinimalGap + r;
        return new Point(offsetX + merged + (split - merged) * _goo, offsetY + h / 2);
    }

    private void LayoutIslandExtras(Geometry geometry, double offsetX, double offsetY, double w, double h, double windowW)
    {
        if (_island == null) return;

        // Expanded shadow: blur 40, y 10, black 45 %, growing and shrinking with the height.
        if (_islandShadowPath != null)
        {
            double k = Math.Clamp((h - IslandMetrics.CompactHeight) / Math.Max(1, DynamicOpenHeight() - IslandMetrics.CompactHeight), 0, 1);
            double opacity = _islandShadowActive ? IslandMetrics.ShadowOpacity * k : 0;
            _islandShadowPath.Data = geometry;
            System.Windows.Controls.Canvas.SetLeft(_islandShadowPath, offsetX);
            System.Windows.Controls.Canvas.SetTop(_islandShadowPath, offsetY + IslandMetrics.ShadowOffsetY);
            _islandShadowPath.Opacity = opacity;
            _islandShadowPath.Visibility = opacity > 0.005 ? Visibility.Visible : Visibility.Collapsed;
        }

        _squashTransform.CenterX = windowW / 2;
        _squashTransform.CenterY = offsetY + h / 2;

        LayoutMinimalCircle(offsetX, offsetY, w, h);
    }
}
