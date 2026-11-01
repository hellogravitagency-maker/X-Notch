using System;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace XNotch.Views.Components;

/// <summary>
/// Renders audio spectrum or perceived loudness waveform as vibrant pill-shaped bars
/// that grow symmetrically from center. Supports auto-gain normalization, Apple Dynamic Island
/// gradient styling, smooth resting dots when paused, and subtle ambient bloom.
/// </summary>
public class AudioVisualizer : FrameworkElement
{
    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register(nameof(BarColor), typeof(Color), typeof(AudioVisualizer),
            new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(AudioVisualizer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarCountProperty =
        DependencyProperty.Register(nameof(BarCount), typeof(int), typeof(AudioVisualizer),
            new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarWidthProperty =
        DependencyProperty.Register(nameof(BarWidth), typeof(double), typeof(AudioVisualizer),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarGapProperty =
        DependencyProperty.Register(nameof(BarGap), typeof(double), typeof(AudioVisualizer),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double BarGap
    {
        get => (double)GetValue(BarGapProperty);
        set => SetValue(BarGapProperty, value);
    }

    // Display-ready levels (one per visual bar)
    private float[] _display = new float[4];
    // Auto-gain: tracks recent peak per bar for normalization
    private float[] _peak = new float[4];
    // Cached frozen brush for single-color fallback
    private SolidColorBrush? _cachedSolidBrush;
    private Color _cachedSolidColor;

    // Apple Music signature sunset gradient
    private static readonly LinearGradientBrush AppleMusicSunsetBrush;

    static AudioVisualizer()
    {
        AppleMusicSunsetBrush = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0x37, 0x5F), // Neon Coral Red
            Color.FromRgb(0xFF, 0x9F, 0x0A), // Electric Amber
            new Point(0.5, 0),
            new Point(0.5, 1));
        AppleMusicSunsetBrush.Freeze();
    }

    public void ResetToIdle()
    {
        for (int i = 0; i < _display.Length; i++)
        {
            _display[i] = 0.0f;
        }
        InvalidateVisual();
    }

    public void UpdateSpectrum(float[] data)
    {
        int barCount = BarCount;
        if (_display.Length != barCount)
        {
            _display = new float[barCount];
            _peak = new float[barCount];
            for (int i = 0; i < barCount; i++) _peak[i] = 0.3f;
        }

        int bandsPerBar = Math.Max(1, data.Length / barCount);

        for (int i = 0; i < barCount; i++)
        {
            int start = i * bandsPerBar;
            int end = Math.Min(start + bandsPerBar, data.Length);
            float sum = 0;
            int count = 0;
            for (int j = start; j < end; j++)
            {
                sum += data[j];
                count++;
            }
            float avg = count > 0 ? sum / count : 0;

            if (avg > _peak[i])
                _peak[i] = _peak[i] * 0.5f + avg * 0.5f;
            else
                _peak[i] = _peak[i] * 0.998f;
            _peak[i] = Math.Max(_peak[i], 0.15f);

            float normalized = Math.Min(0.85f, avg / _peak[i] * 0.85f);

            // Responsive spring attack, organic fluid release
            if (normalized > _display[i])
                _display[i] = _display[i] * 0.25f + normalized * 0.75f;
            else
                _display[i] = _display[i] * 0.84f + normalized * 0.16f;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Renders symmetric perceived loudness waveform from ring buffer.
    /// Newest sample in the center, radiating outward to the edges.
    /// </summary>
    public void UpdateWaveform(float[] ringBuffer)
    {
        int barCount = BarCount;
        if (_display.Length != barCount)
        {
            _display = new float[barCount];
            _peak = new float[barCount];
            for (int i = 0; i < barCount; i++) _peak[i] = 0.3f;
        }

        int center = barCount / 2;
        int bufLen = ringBuffer.Length;

        for (int i = 0; i < barCount; i++)
        {
            int dist = Math.Abs(i - center);
            int sampleIdx = (bufLen - 1 - (dist * 4)) % bufLen;
            if (sampleIdx < 0) sampleIdx += bufLen;

            float val = ringBuffer[sampleIdx];
            float edgeFactor = 1.0f - (float)dist / (center + 1) * 0.25f;
            float normalized = Math.Clamp(val * edgeFactor, 0.05f, 0.95f);

            if (normalized > _display[i])
                _display[i] = _display[i] * 0.3f + normalized * 0.7f;
            else
                _display[i] = _display[i] * 0.82f + normalized * 0.18f;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double h = ActualHeight;
        int count = BarCount;
        double bw = BarWidth;
        double gap = BarGap;

        if (count <= 0 || h <= 0) return;

        double pillRadius = bw / 2.0;
        double minH = bw;           // resting pill = a perfect sphere dot
        double maxH = h;

        double totalW = count * bw + (count - 1) * gap;
        double startX = (ActualWidth - totalW) / 2.0;
        double centerY = h / 2.0;

        // Choose brush: custom BarBrush > dynamic BarColor (if not white) > AppleMusicSunsetBrush
        Brush brush;
        if (BarBrush != null)
        {
            brush = BarBrush;
        }
        else if (BarColor != Colors.White && BarColor.A > 0)
        {
            if (_cachedSolidBrush == null || _cachedSolidColor != BarColor)
            {
                _cachedSolidBrush = new SolidColorBrush(BarColor);
                _cachedSolidBrush.Freeze();
                _cachedSolidColor = BarColor;
            }
            brush = _cachedSolidBrush;
        }
        else
        {
            brush = AppleMusicSunsetBrush;
        }

        for (int i = 0; i < count; i++)
        {
            float level = (i < _display.Length) ? _display[i] : 0;
            double barH = minH + level * (maxH - minH);

            double x = startX + i * (bw + gap);
            double y = centerY - barH / 2.0;

            var rect = new Rect(x, y, bw, barH);
            dc.DrawRoundedRectangle(brush, null, rect, pillRadius, pillRadius);
        }
    }
}
