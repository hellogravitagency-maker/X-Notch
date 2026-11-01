using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.CoreAudioApi.Interfaces;

namespace XNotch.Services;

/// <summary>
/// Captures system audio output via WASAPI loopback and computes both FFT
/// spectrum data and an authentic 6-band perceived loudness DSP waveform.
/// </summary>
public class AudioCaptureService : IDisposable, IMMNotificationClient
{
    private WasapiLoopbackCapture? _capture;
    private readonly float[] _fftBuffer;
    private readonly float[] _hanningWindow;
    private readonly NAudio.Dsp.Complex[] _complexBuffer;
    private readonly int _fftSize;
    private int _fftPos;
    private bool _isCapturing;
    private bool _disposed;
    private readonly object _captureLock = new();
    private MMDeviceEnumerator? _enumerator;

    // 6-Band Loudness Waveform DSP
    private readonly LoudnessWaveformAnalyzer _loudnessAnalyzer = new();

    /// <summary>
    /// Spectrum levels (0..1) for each frequency band.
    /// Updated on each FFT cycle. Thread-safe reads expected from UI.
    /// </summary>
    public float[] SpectrumData { get; }

    /// <summary>Number of output frequency bands.</summary>
    public int BandCount { get; }

    /// <summary>
    /// 48-slot circular ring buffer of perceived loudness waveform samples (~480ms of history).
    /// </summary>
    public float[] WaveformRingBuffer => _loudnessAnalyzer.WaveformRingBuffer;

    /// <summary>
    /// Current instantaneous perceived loudness level (0..1).
    /// </summary>
    public float CurrentLoudness => _loudnessAnalyzer.CurrentLevel;

    public event Action? SpectrumUpdated;
    public event Action? WaveformUpdated;

    public AudioCaptureService(int bandCount = 12, int fftSize = 2048)
    {
        BandCount = bandCount;
        _fftSize = fftSize;
        _fftBuffer = new float[fftSize];
        _complexBuffer = new NAudio.Dsp.Complex[fftSize];
        _hanningWindow = new float[fftSize];
        SpectrumData = new float[bandCount];

        // Precompute Hanning window to eliminate trigonometric math on every audio buffer
        for (int i = 0; i < fftSize; i++)
        {
            _hanningWindow[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1))));
        }
    }

    public void Start()
    {
        lock (_captureLock)
        {
            if (_isCapturing || _disposed) return;

            try
            {
                if (_enumerator == null)
                {
                    try
                    {
                        _enumerator = new MMDeviceEnumerator();
                        _enumerator.RegisterEndpointNotificationCallback(this);
                    }
                    catch { }
                }
                _capture = new WasapiLoopbackCapture();
                _loudnessAnalyzer.Reset(_capture.WaveFormat.SampleRate);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isCapturing = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioCapture start failed: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_captureLock)
        {
            if (!_isCapturing) return;
            _isCapturing = false;

            try
            {
                _capture?.StopRecording();
            }
            catch { }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isCapturing || _capture == null) return;

        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int channels = _capture.WaveFormat.Channels;
        int sampleCount = e.BytesRecorded / (bytesPerSample * channels);

        for (int i = 0; i < sampleCount; i++)
        {
            int offset = i * bytesPerSample * channels;
            float sample;

            if (channels >= 2)
            {
                // Mix down to mono
                float s0 = ReadSample(e.Buffer, offset, bytesPerSample);
                float s1 = ReadSample(e.Buffer, offset + bytesPerSample, bytesPerSample);
                sample = 0.5f * (s0 + s1);
            }
            else
            {
                sample = ReadSample(e.Buffer, offset, bytesPerSample);
            }

            // Feed Loudness DSP Analyzer
            _loudnessAnalyzer.ProcessSample(sample);

            // Feed FFT Buffer
            _fftBuffer[_fftPos++] = sample;
            if (_fftPos >= _fftSize)
            {
                _fftPos = 0;
                ProcessFft();
            }
        }

        if (_loudnessAnalyzer.CheckNewSampleAvailable())
        {
            WaveformUpdated?.Invoke();
        }
    }

    private static float ReadSample(byte[] buffer, int offset, int bytesPerSample) => bytesPerSample switch
    {
        4 => BitConverter.ToSingle(buffer, offset), // IEEE float
        2 => BitConverter.ToInt16(buffer, offset) / 32768f, // 16-bit PCM
        _ => 0f
    };

    private void ProcessFft()
    {
        // Silence detection: bypass FFT calculation when audio is silent
        float maxVal = 0f;
        for (int i = 0; i < _fftSize; i++)
        {
            float abs = Math.Abs(_fftBuffer[i]);
            if (abs > maxVal) maxVal = abs;
        }

        if (maxVal < 0.0001f)
        {
            bool hadNonZero = false;
            for (int i = 0; i < BandCount; i++)
            {
                if (SpectrumData[i] > 0.01f)
                {
                    SpectrumData[i] *= 0.5f;
                    hadNonZero = true;
                }
                else
                {
                    SpectrumData[i] = 0f;
                }
            }
            if (hadNonZero) SpectrumUpdated?.Invoke();
            return;
        }

        // Apply precomputed Hanning window into reusable complex buffer (zero heap allocation)
        for (int i = 0; i < _fftSize; i++)
        {
            _complexBuffer[i].X = _fftBuffer[i] * _hanningWindow[i];
            _complexBuffer[i].Y = 0;
        }

        // In-place FFT
        int m = (int)Math.Log2(_fftSize);
        NAudio.Dsp.FastFourierTransform.FFT(true, m, _complexBuffer);

        // Compute magnitude for each band (log frequency mapping)
        int halfN = _fftSize / 2;
        float maxFreqIdx = halfN;

        for (int band = 0; band < BandCount; band++)
        {
            float lowFrac = (float)Math.Pow(band / (float)BandCount, 2.0);
            float highFrac = (float)Math.Pow((band + 1) / (float)BandCount, 2.0);

            int lowBin = Math.Max(1, (int)(lowFrac * maxFreqIdx));
            int highBin = Math.Min(halfN - 1, (int)(highFrac * maxFreqIdx));
            if (highBin <= lowBin) highBin = lowBin + 1;

            float sum = 0;
            for (int j = lowBin; j <= highBin; j++)
            {
                float mag = (float)Math.Sqrt(_complexBuffer[j].X * _complexBuffer[j].X + _complexBuffer[j].Y * _complexBuffer[j].Y);
                sum = Math.Max(sum, mag);
            }

            // Convert to dB scale (0..1)
            float db = 20f * (float)Math.Log10(Math.Max(sum, 1e-10f));
            float normalized = Math.Clamp((db + 60f) / 60f, 0f, 1f);

            // Smooth with previous value
            SpectrumData[band] = SpectrumData[band] * 0.3f + normalized * 0.7f;
        }

        SpectrumUpdated?.Invoke();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Array.Clear(SpectrumData, 0, SpectrumData.Length);
        _loudnessAnalyzer.Clear();
        SpectrumUpdated?.Invoke();
        WaveformUpdated?.Invoke();

        bool unexpected;
        lock (_captureLock)
        {
            unexpected = _isCapturing && !_disposed;
            if (unexpected) _isCapturing = false;
        }

        if (unexpected)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1500);
                lock (_captureLock)
                {
                    if (_disposed || _isCapturing) return;
                    try { _capture?.Dispose(); } catch { }
                    _capture = null;
                }
                Start();
            });
        }
    }

    public void Dispose()
    {
        lock (_captureLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        _capture?.Dispose();
        if (_enumerator != null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            _enumerator.Dispose();
            _enumerator = null;
        }
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && (role == Role.Multimedia || role == Role.Console))
        {
            lock (_captureLock)
            {
                if (!_isCapturing || _disposed) return;
            }
            Stop();
            Start();
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    #region Nested Loudness DSP Analyzer

    private sealed class LoudnessWaveformAnalyzer
    {
        private static readonly float[] CenterFreqs = [80f, 250f, 700f, 2000f, 5000f, 11000f];
        private static readonly float[] Weights = [0.55f, 0.85f, 1.0f, 1.0f, 0.9f, 0.7f];

        private readonly RbjBandPassFilter[] _filters = new RbjBandPassFilter[6];
        private readonly float[] _bandAccum = new float[6];
        public float[] WaveformRingBuffer { get; } = new float[48];

        private int _sampleRate = 48000;
        private int _chunkSize = 480; // ~10ms
        private int _chunkCounter;
        private int _writePos;
        private bool _newSampleFlag;

        private float _refDb = -30f;
        private float _smoothedDensity;
        public float CurrentLevel { get; private set; }

        public LoudnessWaveformAnalyzer()
        {
            for (int i = 0; i < 6; i++)
            {
                _filters[i] = new RbjBandPassFilter();
            }
        }

        public void Reset(int sampleRate)
        {
            _sampleRate = Math.Max(11025, sampleRate);
            _chunkSize = _sampleRate / 100; // 10ms chunks
            _chunkCounter = 0;
            _writePos = 0;
            _refDb = -30f;
            _smoothedDensity = 0.5f;
            CurrentLevel = 0f;

            for (int i = 0; i < 6; i++)
            {
                _bandAccum[i] = 0f;
                _filters[i].Init(CenterFreqs[i], _sampleRate, 1.4f);
            }
            Array.Clear(WaveformRingBuffer, 0, WaveformRingBuffer.Length);
        }

        public void ProcessSample(float sample)
        {
            for (int i = 0; i < 6; i++)
            {
                float filtered = _filters[i].Process(sample);
                _bandAccum[i] += filtered * filtered;
            }

            _chunkCounter++;
            if (_chunkCounter >= _chunkSize)
            {
                _chunkCounter = 0;
                FinishChunk();
            }
        }

        public bool CheckNewSampleAvailable()
        {
            if (_newSampleFlag)
            {
                _newSampleFlag = false;
                return true;
            }
            return false;
        }

        private void FinishChunk()
        {
            float weightedEnergySum = 0f;
            float maxBandEnergy = 0f;
            int activeBands = 0;

            for (int i = 0; i < 6; i++)
            {
                float bandRms = (float)Math.Sqrt(_bandAccum[i] / _chunkSize);
                _bandAccum[i] = 0f;

                weightedEnergySum += bandRms * Weights[i];
                if (bandRms > maxBandEnergy) maxBandEnergy = bandRms;
            }

            float maxDb = 20f * (float)Math.Log10(Math.Max(maxBandEnergy, 1e-6f));
            for (int i = 0; i < 6; i++)
            {
                // Active if within 15 dB of peak band and above -65 dBFS
                float bandDb = 20f * (float)Math.Log10(Math.Max(_bandAccum[i], 1e-6f));
                if (bandDb >= maxDb - 15f && bandDb > -65f) activeBands++;
            }

            float instantDensity = (Math.Max(1, activeBands) - 1f) / 5f;
            _smoothedDensity = _smoothedDensity * 0.92f + instantDensity * 0.08f;

            float loudnessDb = 20f * (float)Math.Log10(Math.Max(weightedEnergySum, 1e-6f));

            // Peak reference decay
            if (loudnessDb > _refDb) _refDb = loudnessDb;
            else _refDb = Math.Max(-60f, _refDb - 0.03f); // ~3 dB/sec decay

            // Auto-gain boost for quiet recordings (max +8 dB)
            float boost = Math.Clamp(-14f - _refDb, 0f, 8f);
            float level = ((loudnessDb + boost) - (-60f)) / 50f;
            level = Math.Clamp(level, 0f, 1f);
            level = (float)Math.Pow(level, 1.3) * (0.6f + 0.4f * _smoothedDensity);

            if (loudnessDb < -70f) level = 0f;

            // Attack/Release envelope
            if (level > CurrentLevel) CurrentLevel = CurrentLevel * 0.3f + level * 0.7f;
            else CurrentLevel = CurrentLevel * 0.85f + level * 0.15f;

            WaveformRingBuffer[_writePos] = CurrentLevel;
            _writePos = (_writePos + 1) % WaveformRingBuffer.Length;
            _newSampleFlag = true;
        }

        public void Clear()
        {
            Array.Clear(WaveformRingBuffer, 0, WaveformRingBuffer.Length);
            CurrentLevel = 0f;
        }
    }

    private sealed class RbjBandPassFilter
    {
        private float _b0, _b2, _a1, _a2;
        private float _x1, _x2, _y1, _y2;

        public void Init(float f0, int fs, float q)
        {
            _x1 = _x2 = _y1 = _y2 = 0f;
            if (f0 >= fs * 0.45f)
            {
                _b0 = 0f; _b2 = 0f; _a1 = 0f; _a2 = 0f;
                return;
            }

            double w0 = 2.0 * Math.PI * f0 / fs;
            double alpha = Math.Sin(w0) / (2.0 * q);
            double cosW0 = Math.Cos(w0);

            double a0 = 1.0 + alpha;
            _b0 = (float)(alpha / a0);
            _b2 = (float)(-alpha / a0);
            _a1 = (float)(-2.0 * cosW0 / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }

        public float Process(float inSample)
        {
            float outSample = _b0 * inSample + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = inSample;
            _y2 = _y1;
            _y1 = outSample;
            return outSample;
        }
    }

    #endregion
}
