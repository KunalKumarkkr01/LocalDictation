using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Pipeline;
using LocalDictation.Domain;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Desktop.Services;

/// <summary>
/// The interaction controller that turns a hotkey press into a full dictation: inspect the
/// focused target, enforce privacy blocks, show the overlay, capture audio (with VAD auto-stop
/// and ESC cancel), run the pipeline, and report the outcome.
/// </summary>
/// <remarks>
/// This is the Desktop-layer conductor sitting above the Application <see cref="DictationPipeline"/>.
/// A single-slot guard prevents overlapping sessions; a fresh <see cref="CancellationTokenSource"/>
/// per session threads ESC/cancel through capture, transcription and AI processing.
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
        _capture.SilenceDetected += (_, _) => _ = FinishAsync();
        _capture.LevelChanged += (_, lvl) => _overlay.UpdateLevel(lvl);

        if (!_hotkey.Register(_settings.Hotkey))
            _notify.Error("Hotkey unavailable", $"'{_settings.Hotkey}' is in use by another app. Change it in Settings.");

        _ = Task.Run(async () =>
        {
            try { await _speech.WarmUpAsync(); _log.LogInformation("Speech engine warmed."); }
            catch (Exception ex) { _log.LogWarning(ex, "Warm-up failed."); }
        });
    }

    /// <summary>Toggles: start recording, or finish an in-progress recording.</summary>
    public void TriggerManually() => OnHotkey();

    private void OnHotkey()
    {
        if (_recording) _ = FinishAsync();
        else _ = StartAsync();
    }

    private async Task StartAsync()
    {
        if (!await _slot.WaitAsync(0)) return; // a session is already mid-flight
        try
        {
            var target = _inspector.CaptureFocusedTarget();

            if (target.IsSensitive || IsBlocked(target))
            {
                _notify.Info("Dictation blocked", $"{target.ProcessName} is a protected field.");
                _slot.Release();
                return;
            }

            _target = target;
            _cts = new CancellationTokenSource();
            _recording = true;
            _overlay.Show(target);
            _capture.Start();
            _log.LogInformation("Recording started for {App}", target.ProcessName);
        }
        catch (Exception ex)
        {
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

            if (clip.IsEmpty)
            {
                _notify.Info("Nothing captured", "No audio was recorded.");
                return;
            }

            var outcome = await _pipeline.RunAsync(clip, _target ?? TargetControl.Unknown, _settings, ct);
            if (!ct.IsCancellationRequested)
                _notify.Info("Dictation complete", outcome.Message);
        }
        catch (OperationCanceledException) { /* cancelled */ }
        catch (Exception ex)
        {
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

    /// <inheritdoc />
    public void Dispose()
    {
        _hotkey.Unregister();
        _slot.Dispose();
    }
}
