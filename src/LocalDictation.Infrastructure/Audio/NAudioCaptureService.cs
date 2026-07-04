using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
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
    private const double SpeechThreshold = 0.018; // RMS above which we consider speech present
    private const double SilenceThreshold = 0.010; // RMS below which we consider silence

    private readonly AppSettings _settings;
    private readonly ILogger<NAudioCaptureService> _log;
    private readonly object _lock = new();

    private WaveInEvent? _waveIn;
    private List<float> _buffer = new();
    private bool _speechStarted;
    private DateTime _lastSpeechAt;
    private bool _capturing;

    /// <inheritdoc />
    public event EventHandler<double>? LevelChanged;
    /// <inheritdoc />
    public event EventHandler? SilenceDetected;

    /// <summary>Creates the capture service.</summary>
    public NAudioCaptureService(AppSettings settings, ILogger<NAudioCaptureService> log)
    {
        _settings = settings;
        _log = log;
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
            _log.LogInformation("Audio capture stopped: {Sec:F1}s", samples.Length / (double)AudioClip.RequiredSampleRate);
            return new AudioClip(samples);
        }
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
        lock (_lock)
        {
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                float f = s / 32768f;
                _buffer.Add(f);
                sumSq += f * f;
            }
        }

        double rms = sampleCount > 0 ? Math.Sqrt(sumSq / sampleCount) : 0;
        LevelChanged?.Invoke(this, Math.Min(1.0, rms * 6)); // scaled for the meter

        var now = DateTime.UtcNow;
        if (rms >= SpeechThreshold)
        {
            _speechStarted = true;
            _lastSpeechAt = now;
        }
        else if (_speechStarted && rms < SilenceThreshold)
        {
            if ((now - _lastSpeechAt).TotalMilliseconds >= _settings.SilenceTimeoutMs)
            {
                _speechStarted = false; // fire once
                SilenceDetected?.Invoke(this, EventArgs.Empty);
            }
        }
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
