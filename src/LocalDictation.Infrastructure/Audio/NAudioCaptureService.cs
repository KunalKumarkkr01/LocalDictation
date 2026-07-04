using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace LocalDictation.Infrastructure.Audio;

/// <summary>
/// Captures microphone audio via NAudio and produces a 16 kHz mono float
/// <see cref="AudioClip"/> ready for Whisper, with an energy-based VAD that auto-stops
/// after trailing silence.
/// </summary>
/// <remarks>
/// Records directly at 16 kHz mono 16-bit PCM (the format Whisper needs), which the vast
/// majority of Windows capture drivers support, avoiding a resampling stage. A simple RMS
/// VAD is used as the shipping default; Silero-ONNX VAD is a drop-in upgrade behind the
/// same interface (design §8).
/// </remarks>
public sealed class NAudioCaptureService : IAudioCaptureService
{
    // Calibrated against a quiet laptop mic array (measured noise floor ~0.001 RMS). Thresholds
    // sit a few multiples above the floor so normal, close speech reliably registers.
    private const double SpeechThreshold = 0.006;  // RMS above which we consider speech present
    private const double SilenceThreshold = 0.003; // RMS below which we consider silence

    private readonly AppSettings _settings;
    private readonly ILogger<NAudioCaptureService> _log;
    private readonly object _lock = new();

    private const int MinRecordingMs = 900; // never auto-stop before this, so short pauses don't cut speech

    private WaveInEvent? _waveIn;
    private List<float> _buffer = new();
    private bool _speechStarted;
    private DateTime _lastSpeechAt;
    private DateTime _startedAt;
    private bool _capturing;

    // ---- Spectrum analysis (drives the frequency-reactive waveform) ----
    private const int FftSize = 512;      // 2^FftM; ~31 Hz/bin at 16 kHz
    private const int FftM = 9;
    private const int Bands = 13;         // one per waveform bar
    private const float SpectrumGain = 15f;
    private readonly float[] _hann = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly int[] _bandBins = new int[Bands + 1];

    /// <inheritdoc />
    public event EventHandler<double>? LevelChanged;
    /// <inheritdoc />
    public event EventHandler<float[]>? SpectrumChanged;
    /// <inheritdoc />
    public event EventHandler? SilenceDetected;

    /// <summary>Creates the capture service.</summary>
    public NAudioCaptureService(AppSettings settings, ILogger<NAudioCaptureService> log)
    {
        _settings = settings;
        _log = log;

        for (int i = 0; i < FftSize; i++)
            _hann[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));

        // Log-spaced band edges over the voice range (~80 Hz .. 4 kHz), as FFT bin indices.
        const double lowHz = 80, highHz = 4000;
        double binHz = (double)AudioClip.RequiredSampleRate / FftSize;
        for (int k = 0; k <= Bands; k++)
        {
            double hz = lowHz * Math.Pow(highHz / lowHz, k / (double)Bands);
            _bandBins[k] = Math.Clamp((int)Math.Round(hz / binHz), 1, FftSize / 2 - 1);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetInputDevices()
    {
        var names = new List<string>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                names.Add(d.FriendlyName);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Device enumeration failed."); }
        return names;
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_lock)
        {
            if (_capturing) return;
            _buffer = new List<float>(AudioClip.RequiredSampleRate * 8);
            _speechStarted = false;
            _lastSpeechAt = DateTime.UtcNow;
            _startedAt = DateTime.UtcNow;

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(AudioClip.RequiredSampleRate, 16, 1),
                DeviceNumber = ResolveDeviceNumber(_settings.MicrophoneDevice),
                BufferMilliseconds = 50
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            _capturing = true;
            _log.LogInformation("Audio capture started on device #{Dev}", _waveIn.DeviceNumber);
        }
    }

    /// <inheritdoc />
    public AudioClip Stop()
    {
        lock (_lock)
        {
            StopInternal();
            var samples = _buffer.ToArray();
            var peak = Normalize(samples);
            _log.LogInformation("Audio capture stopped: {Sec:F1}s (peak {Peak:F3})",
                samples.Length / (double)AudioClip.RequiredSampleRate, peak);
            return new AudioClip(samples);
        }
    }

    /// <summary>
    /// Peak-normalizes the buffer so quiet microphones still feed Whisper a clear signal.
    /// Only boosts when there's real signal (peak above the noise floor) and never attenuates,
    /// so speech is amplified while near-silence is left alone (avoids amplifying pure noise).
    /// </summary>
    private static float Normalize(float[] samples)
    {
        const float target = 0.95f;
        const float minPeak = 0.01f; // below this it's basically silence — don't amplify noise
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > peak) peak = a;
        }
        if (peak >= minPeak && peak < target)
        {
            float gain = target / peak;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
        }
        return peak;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        lock (_lock)
        {
            StopInternal();
            _buffer.Clear();
        }
    }

    private void StopInternal()
    {
        if (!_capturing) return;
        try
        {
            _waveIn!.DataAvailable -= OnDataAvailable;
            _waveIn.StopRecording();
            _waveIn.Dispose();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Error stopping capture."); }
        finally { _waveIn = null; _capturing = false; }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // 16-bit PCM -> float, compute RMS for level + VAD.
        int sampleCount = e.BytesRecorded / 2;
        double sumSq = 0;
        bool haveFft = false;
        lock (_lock)
        {
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                float f = s / 32768f;
                _buffer.Add(f);
                sumSq += f * f;
            }

            // Snapshot the most recent window (Hann-tapered) for the spectrum.
            int n = _buffer.Count;
            if (n >= FftSize)
            {
                for (int i = 0; i < FftSize; i++)
                {
                    _fft[i].X = _buffer[n - FftSize + i] * _hann[i];
                    _fft[i].Y = 0f;
                }
                haveFft = true;
            }
        }

        double rms = sampleCount > 0 ? Math.Sqrt(sumSq / sampleCount) : 0;
        // Non-linear (sqrt) mapping with high gain so a quiet mic still moves the meter visibly.
        LevelChanged?.Invoke(this, Math.Min(1.0, Math.Sqrt(rms * 18)));
        if (haveFft && SpectrumChanged is not null)
            SpectrumChanged.Invoke(this, ComputeBands());

        var now = DateTime.UtcNow;
        if (rms >= SpeechThreshold)
        {
            _speechStarted = true;
            _lastSpeechAt = now;
        }
        else if (_speechStarted && rms < SilenceThreshold)
        {
            // Require both a sustained trailing silence AND a minimum total duration, so natural
            // between-sentence pauses never chop a recording in half.
            bool longEnough = (now - _startedAt).TotalMilliseconds >= MinRecordingMs;
            bool silentEnough = (now - _lastSpeechAt).TotalMilliseconds >= _settings.SilenceTimeoutMs;
            if (longEnough && silentEnough)
            {
                _speechStarted = false; // fire once
                SilenceDetected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Runs an FFT over the windowed snapshot in <see cref="_fft"/> and folds it into
    /// <see cref="Bands"/> log-spaced magnitudes (0..1) covering the voice range.
    /// </summary>
    /// <returns>One magnitude per waveform bar.</returns>
    private float[] ComputeBands()
    {
        FastFourierTransform.FFT(true, FftM, _fft);
        var bands = new float[Bands];
        for (int k = 0; k < Bands; k++)
        {
            int lo = _bandBins[k];
            int hi = Math.Max(lo + 1, _bandBins[k + 1]);
            double sum = 0;
            for (int b = lo; b < hi; b++)
            {
                // Amplitude-normalized magnitude (2/N), so values sit in a stable 0..~1 range.
                double mag = Math.Sqrt(_fft[b].X * _fft[b].X + _fft[b].Y * _fft[b].Y) * (2.0 / FftSize);
                sum += mag;
            }
            double avg = sum / (hi - lo);
            double tilt = 1.0 + k * 0.10; // lift higher bands; voice energy skews low
            bands[k] = (float)Math.Min(1.0, Math.Sqrt(avg * SpectrumGain * tilt));
        }
        return bands;
    }

    private int ResolveDeviceNumber(string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName)) return 0;
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            // Product names are truncated to 31 chars by the legacy API — match on prefix.
            if (friendlyName.StartsWith(caps.ProductName, StringComparison.OrdinalIgnoreCase) ||
                caps.ProductName.StartsWith(friendlyName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    /// <inheritdoc />
    public void Dispose() => Cancel();
}
