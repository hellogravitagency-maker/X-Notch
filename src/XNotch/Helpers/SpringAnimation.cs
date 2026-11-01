using System;

namespace XNotch.Helpers;

/// <summary>
/// High-performance spring physics animation solver matching Apple's interactiveSpring.
/// Supports analytical position &amp; velocity evaluation with continuous momentum across
/// animation redirects.
/// 
/// Equation: x(t) = target + e^(-ζωt) * (A·cos(ωd·t) + B·sin(ωd·t))
/// where ζ = damping ratio, ω = natural frequency, ωd = damped frequency.
/// </summary>
public class SpringAnimator
{
    private double _omega;       // Natural frequency (ω = 2π / response)
    private double _zeta;        // Damping ratio (ζ)
    private double _omegaD;      // Damped frequency

    private double _startValue;
    private double _targetValue;
    private double _startVelocity;
    private long _startTimestamp;
    private static readonly double TimestampFrequency = (double)System.Diagnostics.Stopwatch.Frequency;
    private bool _isAnimating;

    public double Response { get; set; } = 0.40;
    public double DampingFraction { get; set; } = 0.82;

    public bool IsAnimating => _isAnimating;
    public double CurrentValue { get; private set; }
    public double CurrentVelocity { get; private set; }
    public double TargetValue => _targetValue;

    public event Action<double>? ValueChanged;
    public event Action? Completed;

    public SpringAnimator() { }

    public SpringAnimator(double response, double dampingFraction)
    {
        Response = response;
        DampingFraction = dampingFraction;
    }

    /// <summary>Creates an animator tuned to Apple Dynamic Island fluid expansion.</summary>
    public static SpringAnimator DynamicIslandExpand(double response = 0.38, double damping = 0.78)
        => new(response, damping);

    /// <summary>Creates an animator tuned to Apple Dynamic Island snappy bezel collapse.</summary>
    public static SpringAnimator DynamicIslandCollapse(double response = 0.32, double damping = 0.88)
        => new(response, damping);

    /// <summary>Creates an animator tuned to Apple Dynamic Island tactile peek hover.</summary>
    public static SpringAnimator DynamicIslandPeek(double response = 0.28, double damping = 0.72)
        => new(response, damping);

    /// <summary>Creates an animator tuned to Apple iOS haptic micro-interactions (button taps).</summary>
    public static SpringAnimator Haptic(double response = 0.18, double damping = 0.65)
        => new(response, damping);

    /// <summary>Configures the spring physics parameters in one call.</summary>
    public void SetPhysics(double response, double dampingFraction)
    {
        Response = response;
        DampingFraction = dampingFraction;
    }

    /// <summary>Immediately forces the spring to a settled value without animating.</summary>
    public void Reset(double value)
    {
        CurrentValue = value;
        _targetValue = value;
        _startValue = value;
        _startVelocity = 0;
        CurrentVelocity = 0;
        _isAnimating = false;
        ValueChanged?.Invoke(value);
    }

    /// <summary>
    /// Starts animating towards target. If velocity is not specified and an animation
    /// is already in progress, inherits the current instantaneous velocity for continuous fluid motion.
    /// </summary>
    public void AnimateTo(double target, double currentValue, double? velocity = null)
    {
        _startValue = currentValue;
        _targetValue = target;
        // Inherit ongoing velocity when redirected mid-flight for authentic iOS continuity
        _startVelocity = velocity ?? (_isAnimating ? CurrentVelocity : 0.0);
        _startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        _omega = 2.0 * Math.PI / Math.Max(Response, 0.01);
        _zeta = DampingFraction;
        _omegaD = _omega * Math.Sqrt(Math.Max(1.0 - _zeta * _zeta, 0.0001));

        _isAnimating = true;
        CurrentValue = currentValue;
        CurrentVelocity = _startVelocity;
    }

    /// <summary>
    /// Call this every frame (e.g., from CompositionTarget.Rendering).
    /// Returns current interpolated value. Pass simulatedTimeSeconds >= 0 for deterministic testing.
    /// </summary>
    public double Tick(double simulatedTimeSeconds = -1)
    {
        if (!_isAnimating)
            return CurrentValue;

        double t = simulatedTimeSeconds >= 0
            ? simulatedTimeSeconds
            : (System.Diagnostics.Stopwatch.GetTimestamp() - _startTimestamp) / TimestampFrequency;

        double displacement = _startValue - _targetValue;
        double value;
        double velocity;

        if (_zeta >= 1.0)
        {
            // Critically or over-damped
            double expTerm = Math.Exp(-_omega * _zeta * t);
            double A = displacement;
            double B = _startVelocity + _omega * _zeta * displacement;
            value = _targetValue + expTerm * (A + B * t);
            velocity = expTerm * (B - _omega * _zeta * (A + B * t));
        }
        else
        {
            // Under-damped (elastic spring with natural overshoot)
            double expTerm = Math.Exp(-_zeta * _omega * t);
            double A = displacement;
            double B = (_startVelocity + _zeta * _omega * displacement) / _omegaD;
            double sinVal = Math.Sin(_omegaD * t);
            double cosVal = Math.Cos(_omegaD * t);
            value = _targetValue + expTerm * (A * cosVal + B * sinVal);
            velocity = expTerm * ((B * _omegaD - A * _zeta * _omega) * cosVal - (A * _omegaD + B * _zeta * _omega) * sinVal);
        }

        CurrentValue = value;
        CurrentVelocity = velocity;
        ValueChanged?.Invoke(value);

        // Exact physical settling condition: within threshold and velocity dissipated
        if (Math.Abs(value - _targetValue) < 0.20 && Math.Abs(velocity) < 0.5 && t > 0.04)
        {
            CurrentValue = _targetValue;
            CurrentVelocity = 0;
            _isAnimating = false;
            ValueChanged?.Invoke(_targetValue);
            Completed?.Invoke();
        }

        return CurrentValue;
    }
}
