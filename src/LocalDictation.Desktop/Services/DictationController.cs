using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Pipeline;
using LocalDictation.Domain;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// The interaction controller that turns a hotkey press into a full dictation: inspect the
/// focused target, enforce privacy blocks, show the overlay, capture audio, run the pipeline,
/// and report the outcome.
/// </summary>
/// <remarks>
/// Interaction is a simple, predictable toggle: press the hotkey to start, press again to stop.
/// A conservative VAD auto-stop is an optional convenience (off by default) so it can never chop
/// speech mid-sentence. ESC cancels. A single-slot guard prevents overlapping sessions.
/// </remarks>
public sealed class DictationController : IDisposable
{
    private readonly IHotkeyService _hotkey;
    private readonly IWindowInspector _inspector;
    private readonly IAudioCaptureService _capture;
    private readonly DictationPipeline _pipeline;
    private readonly IOverlayController _overlay;
    private readonly INotificationService _notify;
    private readonly ISpeechEngine _speech;
    private readonly AppSettings _settings;
    private readonly ILogger<DictationController> _log;

    private readonly SemaphoreSlim _slot = new(1, 1);
    private CancellationTokenSource? _cts;
    private TargetControl? _target;
    private volatile bool _recording;

    /// <summary>Creates the controller from its collaborators.</summary>
    public DictationController(
        IHotkeyService hotkey, IWindowInspector inspector, IAudioCaptureService capture,
        DictationPipeline pipeline, IOverlayController overlay, INotificationService notify,
        ISpeechEngine speech, AppSettings settings, ILogger<DictationController> log)
    {
        _hotkey = hotkey; _inspector = inspector; _capture = capture; _pipeline = pipeline;
        _overlay = overlay; _notify = notify; _speech = speech; _settings = settings; _log = log;
    }

    /// <summary>Registers the hotkey, wires events and warms the speech model in the background.</summary>
    public void Initialize()
    {
        _hotkey.HotkeyPressed += (_, _) => OnHotkey();
        _overlay.Cancelled += (_, _) => CancelSession();
        _capture.SilenceDetected += (_, _) => { if (_settings.AutoStopOnSilence) _ = FinishAsync(); };
        _capture.LevelChanged += (_, lvl) => _overlay.UpdateLevel(lvl);

        RegisterHotkeyWithFallback();

        _ = Task.Run(async () =>
        {
            try { await _speech.WarmUpAsync(); _log.LogInformation("Speech engine warmed."); StartupLog.Write("Speech engine warmed."); }
            catch (Exception ex) { _log.LogWarning(ex, "Warm-up failed."); StartupLog.Write("Warm-up failed: " + ex.Message); }
        });
    }

    /// <summary>Toggles: start recording, or finish an in-progress recording.</summary>
    public void TriggerManually() => OnHotkey();

    private void RegisterHotkeyWithFallback()
    {
        if (_hotkey.Register(_settings.Hotkey))
        {
            StartupLog.Write($"Hotkey registered: {_settings.Hotkey}");
            return;
        }

        var original = _settings.Hotkey;
        foreach (var fallback in new[] { "Ctrl+Shift+Space", "Ctrl+Alt+D", "Ctrl+Alt+Q" })
        {
            if (fallback == original) continue;
            if (_hotkey.Register(fallback))
            {
                _settings.Hotkey = fallback;
                StartupLog.Write($"Hotkey fallback registered: {fallback} (requested '{original}' failed)");
                _notify.Info("Hotkey changed", $"'{original}' was unavailable. Now using {fallback}.");
                return;
            }
        }
        StartupLog.Write($"Hotkey registration FAILED for all candidates (requested '{original}')");
        _notify.Error("Hotkey unavailable", $"'{original}' could not be registered. Set a different one in Settings.");
    }

    private void OnHotkey()
    {
        StartupLog.Write($"Hotkey pressed (recording={_recording}).");
        if (_recording) _ = FinishAsync();
        else _ = StartAsync();
    }

    private async Task StartAsync()
    {
        if (!await _slot.WaitAsync(0)) return; // a session is already mid-flight
        try
        {
            // Start the mic FIRST so we never clip the first word while UI Automation inspects the
            // focused control (UIA can take a few hundred ms). Focus doesn't change during this.
            _capture.Start();

            var target = _inspector.CaptureFocusedTarget();
            if (target.IsSensitive || IsBlocked(target))
            {
                _capture.Cancel();
                _notify.Info("Dictation blocked", $"{target.ProcessName} is a protected field.");
                _slot.Release();
                return;
            }

            _target = target;
            _cts = new CancellationTokenSource();
            _recording = true;
            _overlay.Show(target);
            StartupLog.Write($"Recording started → {target.ProcessName} ({target.Kind}).");
            _log.LogInformation("Recording started for {App}", target.ProcessName);
        }
        catch (Exception ex)
        {
            StartupLog.Write("StartAsync FAILED: " + ex);
            _log.LogError(ex, "Failed to start dictation.");
            _notify.Error("Microphone error", ex.Message);
            ResetAfterFailure();
        }
    }

    private async Task FinishAsync()
    {
        if (!_recording) return;
        _recording = false;
        var ct = _cts?.Token ?? CancellationToken.None;

        try
        {
            _overlay.SetStage(OverlayStage.Transcribing);
            var clip = _capture.Stop();
            StartupLog.Write($"Captured {clip.Duration.TotalSeconds:F1}s, maxRms={MaxRms(clip):F4}.");

            if (clip.IsEmpty || clip.Duration.TotalMilliseconds < 250)
            {
                StartupLog.Write("Clip too short — nothing to transcribe.");
                _notify.Info("Nothing captured", "No audio was recorded.");
                return;
            }

            var outcome = await _pipeline.RunAsync(clip, _target ?? TargetControl.Unknown, _settings, ct);
            var transcript = outcome.Session.Transcript;
            StartupLog.Write($"Transcript: \"{transcript?.RawText}\" → delivered={outcome.Delivered} ({outcome.Message})");

            if (!ct.IsCancellationRequested)
                _notify.Info("Dictation complete", outcome.Message);
        }
        catch (OperationCanceledException) { /* cancelled */ }
        catch (Exception ex)
        {
            StartupLog.Write("FinishAsync FAILED: " + ex);
            _log.LogError(ex, "Dictation failed.");
            _overlay.SetStage(OverlayStage.Error, "Failed");
            _notify.Error("Dictation failed", ex.Message);
        }
        finally
        {
            _overlay.Hide();
            _cts?.Dispose();
            _cts = null;
            _slot.Release();
        }
    }

    private void CancelSession()
    {
        if (!_recording && _cts is null) return;
        _recording = false;
        try { _capture.Cancel(); } catch { /* ignore */ }
        _cts?.Cancel();
        _overlay.Hide();
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        if (_slot.CurrentCount == 0) _slot.Release();
        StartupLog.Write("Session cancelled by user.");
        _log.LogInformation("Session cancelled by user.");
    }

    private void ResetAfterFailure()
    {
        _recording = false;
        _overlay.Hide();
        _cts?.Dispose();
        _cts = null;
        if (_slot.CurrentCount == 0) _slot.Release();
    }

    private bool IsBlocked(TargetControl t) =>
        _settings.BlockedApps.Any(b => t.ProcessName.Contains(b, StringComparison.OrdinalIgnoreCase));

    /// <summary>Peak RMS of a clip, for diagnosing whether the mic actually captured speech.</summary>
    private static double MaxRms(AudioClip clip)
    {
        const int window = 1600; // 100 ms
        double max = 0;
        for (int i = 0; i < clip.Samples.Length; i += window)
        {
            double sumSq = 0; int n = Math.Min(window, clip.Samples.Length - i);
            for (int j = 0; j < n; j++) sumSq += clip.Samples[i + j] * (double)clip.Samples[i + j];
            if (n > 0) max = Math.Max(max, Math.Sqrt(sumSq / n));
        }
        return max;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _hotkey.Unregister();
        _slot.Dispose();
    }
}
