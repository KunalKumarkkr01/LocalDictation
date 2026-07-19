using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Mac.Interop;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Mac.Audio;

/// <summary>
/// macOS microphone capture via the CoreAudio <c>AudioQueue</c> C API — the counterpart of the
/// Windows <c>NAudioCaptureService</c>. Records 16 kHz mono float PCM straight into the format Whisper
/// needs, raising input-level and 13-band spectrum events for the capsule meter and auto-stopping on
/// trailing silence when VAD is enabled.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class CoreAudioCaptureService : IAudioCaptureService
{
    private const int BufferBytes = 8192;          // ~0.12 s per buffer at 16 kHz float
    private const int BufferCount = 3;

    private readonly AppSettings _settings;
    private readonly ILogger<CoreAudioCaptureService> _log;
    private readonly SpectrumAnalyzer _spectrum = new();
    private readonly object _lock = new();

    // Keep the delegate + this handle alive for the native queue's lifetime.
    private readonly AudioToolbox.AudioQueueInputCallback _callback;
    private GCHandle _self;

    private IntPtr _queue;
    private readonly List<float> _samples = new();
    private bool _capturing;
    private DateTime _lastVoiceUtc;

    /// <inheritdoc />
    public event EventHandler<double>? LevelChanged;
    /// <inheritdoc />
    public event EventHandler<float[]>? SpectrumChanged;
    /// <inheritdoc />
    public event EventHandler? SilenceDetected;

    /// <summary>Creates the capture service.</summary>
    public CoreAudioCaptureService(AppSettings settings, ILogger<CoreAudioCaptureService> log)
    {
        _settings = settings;
        _log = log;
        _callback = OnBuffer;
    }

    /// <inheritdoc />
    /// <remarks>CoreAudio device enumeration is not surfaced yet; the system default input is used.</remarks>
    public IReadOnlyList<string> GetInputDevices() => new[] { "Default (system)" };

    /// <inheritdoc />
    /// <remarks>Mute state is not queried via the HAL yet; capture-level silence still surfaces in the meter.</remarks>
    public bool IsInputMuted() => false;

    /// <inheritdoc />
    public void Start()
    {
        lock (_lock)
        {
            if (_capturing) return;
            _samples.Clear();
            _lastVoiceUtc = DateTime.UtcNow;

            var fmt = new AudioToolbox.AudioStreamBasicDescription
            {
                mSampleRate = AudioClip.RequiredSampleRate,
                mFormatID = AudioToolbox.FormatLinearPCM,
                mFormatFlags = AudioToolbox.FormatFlagIsFloat | AudioToolbox.FormatFlagIsPacked,
                mBitsPerChannel = 32,
                mChannelsPerFrame = 1,
                mBytesPerFrame = 4,
                mFramesPerPacket = 1,
                mBytesPerPacket = 4,
            };

            _self = GCHandle.Alloc(this);
            int st = AudioToolbox.AudioQueueNewInput(
                ref fmt, _callback, GCHandle.ToIntPtr(_self), IntPtr.Zero, IntPtr.Zero, 0, out _queue);
            if (st != 0) { _self.Free(); throw new InvalidOperationException($"AudioQueueNewInput failed ({st})."); }

            for (int i = 0; i < BufferCount; i++)
            {
                if (AudioToolbox.AudioQueueAllocateBuffer(_queue, BufferBytes, out var buf) == 0)
                    AudioToolbox.AudioQueueEnqueueBuffer(_queue, buf, 0, IntPtr.Zero);
            }

            st = AudioToolbox.AudioQueueStart(_queue, IntPtr.Zero);
            if (st != 0) { Dispose(); throw new InvalidOperationException($"AudioQueueStart failed ({st})."); }
            _capturing = true;
            _log.LogInformation("CoreAudio capture started (16 kHz mono).");
        }
    }

    /// <inheritdoc />
    public AudioClip Stop()
    {
        StopQueue();
        lock (_lock)
        {
            var clip = new AudioClip(Normalize(_samples.ToArray()));
            _samples.Clear();
            return clip;
        }
    }

    /// <inheritdoc />
    public void Cancel()
    {
        StopQueue();
        lock (_lock) { _samples.Clear(); }
    }

    /// <summary>
    /// Stops and disposes the queue. <c>AudioQueueStop(queue, true)</c> synchronously blocks until any
    /// buffer callback in flight on CoreAudio's own thread returns — so <see cref="_lock"/> must be
    /// released before making that call, not held across it: <see cref="Consume"/> takes the same lock
    /// to append its chunk, and holding it here would deadlock the native stop against our own callback
    /// (observed as a ~29s stall in practice, resolved only by CoreAudio's internal teardown timeout).
    /// </summary>
    private void StopQueue()
    {
        IntPtr queue;
        lock (_lock)
        {
            if (!_capturing) return;
            _capturing = false;
            queue = _queue;
            _queue = IntPtr.Zero;
        }

        if (queue != IntPtr.Zero)
        {
            AudioToolbox.AudioQueueStop(queue, true);
            AudioToolbox.AudioQueueDispose(queue, true);
        }
        if (_self.IsAllocated) _self.Free();
    }

    /// <summary>Native input callback — routed back to the owning instance via the GCHandle.</summary>
    private static void OnBuffer(IntPtr userData, IntPtr aq, IntPtr bufferPtr, IntPtr startTime, uint numPackets, IntPtr packetDescs)
    {
        var handle = GCHandle.FromIntPtr(userData);
        if (handle.Target is not CoreAudioCaptureService self) return;
        self.Consume(aq, bufferPtr);
    }

    private void Consume(IntPtr aq, IntPtr bufferPtr)
    {
        var buffer = Marshal.PtrToStructure<AudioToolbox.AudioQueueBuffer>(bufferPtr);
        int count = (int)(buffer.mAudioDataByteSize / 4);
        if (count > 0 && buffer.mAudioData != IntPtr.Zero)
        {
            var chunk = new float[count];
            Marshal.Copy(buffer.mAudioData, chunk, 0, count);

            double rms = 0;
            for (int i = 0; i < count; i++) rms += chunk[i] * chunk[i];
            rms = Math.Sqrt(rms / count);

            lock (_lock)
            {
                if (!_capturing) return;
                _samples.AddRange(chunk);
                if (rms > 0.01) _lastVoiceUtc = DateTime.UtcNow;
            }

            LevelChanged?.Invoke(this, Math.Clamp(rms * 4, 0, 1));
            SpectrumChanged?.Invoke(this, _spectrum.Compute(chunk));

            if (_settings.AutoStopOnSilence &&
                (DateTime.UtcNow - _lastVoiceUtc).TotalMilliseconds > _settings.SilenceTimeoutMs)
                SilenceDetected?.Invoke(this, EventArgs.Empty);
        }

        // Recycle the buffer while still recording.
        if (_capturing) AudioToolbox.AudioQueueEnqueueBuffer(aq, bufferPtr, 0, IntPtr.Zero);
    }

    /// <summary>Peak-normalises to reduce the chance of a too-quiet clip transcribing as silence.</summary>
    private static float[] Normalize(float[] samples)
    {
        if (samples.Length == 0) return samples;
        float peak = 0;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        if (peak < 1e-4f) return samples;
        float gain = Math.Min(1f / peak, 8f);
        if (gain <= 1.01f) return samples;
        for (int i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
        return samples;
    }

    /// <inheritdoc />
    public void Dispose() => StopQueue();
}
