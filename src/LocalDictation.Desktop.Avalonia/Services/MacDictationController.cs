using System.Diagnostics;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Pipeline;
using LocalDictation.Desktop.Avalonia.Views;
using LocalDictation.Domain;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Services;

/// <summary>
/// Turns a hotkey press into a full dictation on macOS: inspect the focused target, enforce privacy
/// blocks, show the capsule overlay, capture audio, run the pipeline, and report the outcome. Port of
/// the Windows <c>DictationController</c>; the only platform-specific bit is the clipboard copy (done
/// via <c>/usr/bin/pbcopy</c>).
/// </summary>
/// <remarks>
/// Interaction is a simple toggle: press the hotkey to start, press again to stop. VAD auto-stop is an
/// optional convenience (off by default). ESC cancels. A single-slot guard prevents overlapping sessions.
/// </remarks>
public sealed class MacDictationController : IDisposable
{
    private readonly IHotkeyService _hotkey;
    private readonly IWindowInspector _inspector;
    private readonly IAudioCaptureService _capture;
    private readonly DictationPipeline _pipeline;
    private readonly IOverlayController _overlay;
    private readonly INotificationService _notify;
    private readonly ISpeechEngine _speech;
    private readonly IReadinessService _readiness;
    private readonly IUiDispatcher _ui;
    private readonly AppSettings _settings;
    private readonly IPersonaPicker _picker;
    private readonly PersonaSettings _personas;
    private readonly ILogger<MacDictationController> _log;

    private readonly SemaphoreSlim _slot = new(1, 1);
    private CancellationTokenSource? _cts;
    private TargetControl? _target;
    private TargetControl? _pendingTarget;
    private Persona? _pendingPersona;
    private volatile bool _recording;
    private volatile bool _pickerShowing;

    /// <summary>Creates the controller from its collaborators.</summary>
    public MacDictationController(
        IHotkeyService hotkey, IWindowInspector inspector, IAudioCaptureService capture,
        DictationPipeline pipeline, IOverlayController overlay, INotificationService notify,
        ISpeechEngine speech, IReadinessService readiness, IUiDispatcher ui, AppSettings settings,
        IPersonaPicker picker, PersonaSettings personas, ILogger<MacDictationController> log)
    {
        _hotkey = hotkey; _inspector = inspector; _capture = capture; _pipeline = pipeline;
        _overlay = overlay; _notify = notify; _speech = speech; _readiness = readiness;
        _ui = ui; _settings = settings; _picker = picker; _personas = personas; _log = log;
    }

    // Leave the last dictation on the macOS pasteboard so it can be re-pasted (e.g. when a terminal
    // truncates a long insertion). Uses pbcopy rather than the Avalonia clipboard, which requires a
    // live TopLevel that this headless menu-bar app may not have.
    private void CopyToClipboard(string text)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/pbcopy") { RedirectStandardInput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p is null) return;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit(2000);
        }
        catch (Exception ex) { StartupLog.Write("Clipboard copy failed: " + ex.Message); }
    }

    /// <summary>Registers the hotkey, wires events and warms the speech model in the background.</summary>
    public void Initialize()
    {
        _hotkey.HotkeyPressed += (_, e) =>
        {
            if (e.Action == HotkeyAction.Picker) _ = OnPickerHotkeyAsync();
            else OnHotkey();
        };
        _overlay.Cancelled += (_, _) => CancelSession();
        _capture.SilenceDetected += (_, _) => { if (_settings.AutoStopOnSilence) _ = FinishAsync(); };
        _capture.LevelChanged += (_, lvl) => _overlay.UpdateLevel(lvl);
        _capture.SpectrumChanged += (_, bands) => _overlay.UpdateSpectrum(bands);

        RegisterHotkeyWithFallback();

        if (!string.IsNullOrWhiteSpace(_personas.PickerHotkey))
            _hotkey.RegisterPicker(_personas.PickerHotkey);

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

    /// <summary>Picker hotkey: choose a persona, then dictate that one session with AI forced on.</summary>
    private async Task OnPickerHotkeyAsync()
    {
        if (_recording) { _ = FinishAsync(); return; } // second press ends an in-flight dictation
        if (_pickerShowing) return; // palette already open — ignore repeat presses
        _pickerShowing = true;
        try
        {
            // The palette takes keyboard focus, so capture the currently-focused target BEFORE
            // showing it — otherwise StartAsync would end up inspecting the palette itself.
            var target = _inspector.CaptureFocusedTarget();
            if (target.IsSensitive || IsBlocked(target))
            {
                _notify.Info("Dictation blocked", $"{target.ProcessName} is a protected field.");
                return;
            }

            var chosen = await _picker.PickAsync();
            if (chosen is null) return; // cancelled

            if (!_settings.AiEnabled)
                _notify.Info("AI enhancement is off",
                    $"Turn on AI enhancement in Settings to use the \"{chosen.Name}\" persona. Inserting verbatim for now.");

            _pendingPersona = chosen;
            _pendingTarget = target;
            await StartAsync();
            // If StartAsync didn't actually begin recording (slot busy or preflight failed),
            // clear the stash so it can never leak into a later plain-hotkey dictation.
            if (!_recording) { _pendingPersona = null; _pendingTarget = null; }
        }
        finally { _pickerShowing = false; }
    }

    private async Task StartAsync()
    {
        if (!await _slot.WaitAsync(0)) return; // a session is already mid-flight
        try
        {
            // Pre-flight: if the speech engine or microphone is hard-down, recording would dead-end in a
            // confusing "No speech detected". Catch it now and show the real reason + fix.
            if (!await PassesPreflightAsync()) { _pendingTarget = null; _pendingPersona = null; _slot.Release(); return; }

            // Start the mic FIRST so we never clip the first word while the focused control is inspected.
            _capture.Start();

            // The picker flow already captured the target before it stole focus; reuse it instead
            // of re-inspecting now (which would see the palette, not the original app).
            var target = _pendingTarget ?? _inspector.CaptureFocusedTarget();
            _pendingTarget = null;
            if (target.IsSensitive || IsBlocked(target))
            {
                _capture.Cancel();
                _notify.Info("Dictation blocked", $"{target.ProcessName} is a protected field.");
                _pendingPersona = null;
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

            var sw = Stopwatch.StartNew();
            var outcome = await _pipeline.RunAsync(clip, _target ?? TargetControl.Unknown, _settings, ct, _pendingPersona);
            sw.Stop();
            var transcript = outcome.Session.Transcript;
            StartupLog.Write($"Transcript: \"{transcript?.RawText}\" → delivered={outcome.Delivered} in {sw.ElapsedMilliseconds}ms ({outcome.Message})");

            if (!ct.IsCancellationRequested)
            {
                var heard = transcript?.FinalText ?? "";
                if (!string.IsNullOrWhiteSpace(heard))
                {
                    // The per-dictation toast is opt-out; the clipboard copy always happens regardless.
                    if (_settings.NotifyOnComplete)
                    {
                        if (outcome.Failure == DictationFailure.DeliveredToEditor)
                            _notify.Info("Opened editor", "Focus moved, so the text is in the editor to review and copy.");
                        else
                            _notify.Info(outcome.Delivered ? "Dictated" : "Transcribed", heard);
                    }
                    if (outcome.Oversized && _settings.NotifyOnComplete)
                        _notify.Info("Long dictation",
                            "This was long enough that AI cleanup may have trimmed the start — the full raw transcript is on your clipboard.");
                    CopyToClipboard(heard);
                }
                else
                {
                    // No text — show the REAL reason. Hard failures always notify (override the opt-out).
                    var msg = FailureMessages.For(outcome.Failure);
                    if (msg.IsError) _notify.Error(msg.Title, msg.Body);
                    else if (_settings.NotifyOnComplete) _notify.Info(msg.Title, msg.Body);
                }
            }
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
            _pendingPersona = null;
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
        _pendingPersona = null;
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
        _pendingTarget = null;
        _pendingPersona = null;
        if (_slot.CurrentCount == 0) _slot.Release();
    }

    /// <summary>
    /// Verifies the speech engine and microphone are up before recording. On a hard-down component,
    /// shows an error toast with the real reason + fix and returns false so the caller aborts.
    /// </summary>
    /// <returns>True to proceed with recording; false if a dependency is down (already notified).</returns>
    private async Task<bool> PassesPreflightAsync()
    {
        var speech = await _readiness.CheckSpeechAsync();
        if (speech.State == HealthState.Down)
        {
            StartupLog.Write($"Pre-flight blocked: {speech.Component} — {speech.Summary}");
            _notify.Error(speech.Summary, FirstFix(speech));
            return false;
        }

        var mic = await _readiness.CheckMicrophoneAsync();
        if (mic.State == HealthState.Down)
        {
            StartupLog.Write($"Pre-flight blocked: {mic.Component} — {mic.Summary}");
            _notify.Error(mic.Summary, FirstFix(mic));
            return false;
        }
        return true;
    }

    /// <summary>The leading fix step for a component (toast bodies are short; the rest live in Settings).</summary>
    private static string FirstFix(ComponentHealth health) =>
        health.Fixes.Count > 0 ? health.Fixes[0] : "Open Settings › System status for details.";

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
