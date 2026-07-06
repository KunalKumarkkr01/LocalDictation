using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Diagnostics;

/// <summary>
/// Aggregates the health of dictation's dependencies (speech engine, microphone, optional AI) from
/// the services that already know their own state, mapping each to a <see cref="ComponentHealth"/>
/// with user-facing fix steps.
/// </summary>
public sealed class ReadinessService : IReadinessService
{
    private readonly ISpeechEngine _speech;
    private readonly ISpeechModelManager _models;
    private readonly IAudioCaptureService _audio;
    private readonly ITextProcessor _ai;
    private readonly AppSettings _settings;
    private readonly ILogger<ReadinessService> _log;

    /// <summary>Creates the readiness service from the components it inspects.</summary>
    public ReadinessService(
        ISpeechEngine speech, ISpeechModelManager models, IAudioCaptureService audio,
        ITextProcessor ai, AppSettings settings, ILogger<ReadinessService> log)
    {
        _speech = speech; _models = models; _audio = audio; _ai = ai; _settings = settings; _log = log;
    }

    /// <inheritdoc />
    public Task<ComponentHealth> CheckSpeechAsync(CancellationToken ct = default)
    {
        var status = _speech.Status;
        var health = status.State switch
        {
            SpeechReadiness.Ready => Ok("Speech engine", $"Ready — {_speech.Name}"),

            SpeechReadiness.NativeLibraryMissing => Down("Speech engine",
                "Speech library didn't load",
                "Reinstall LocalDictation (the native speech library is missing from this install).",
                "If it persists after reinstall, report it on the project's GitHub."),

            SpeechReadiness.ModelNotInstalled => Down("Speech engine",
                "No speech model installed",
                "Open onboarding, or Settings, and download a Whisper model.",
                "Then press Reload model."),

            SpeechReadiness.LoadFailed => Down("Speech engine",
                "Model failed to load",
                "The model file may be corrupt — re-download it, then press Reload model."),

            // NotWarmedUp: not yet loaded. If the model file is there it just needs a (re)load.
            _ => _models.IsInstalled(_settings.WhisperModel ?? _models.RecommendedForHardware())
                ? Degraded("Speech engine", "Not loaded yet", "Press Reload model to load it now.")
                : Down("Speech engine", "No speech model installed",
                    "Download a Whisper model in onboarding or Settings, then press Reload model."),
        };
        return Task.FromResult(health);
    }

    /// <inheritdoc />
    public Task<ComponentHealth> CheckMicrophoneAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> devices;
        try { devices = _audio.GetInputDevices(); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Microphone enumeration failed.");
            devices = Array.Empty<string>();
        }

        if (devices.Count == 0)
            return Task.FromResult(Down("Microphone", "No microphone found",
                "Connect a microphone or headset.",
                "Check Windows Settings › System › Sound › Input.",
                "Allow microphone access under Windows Settings › Privacy & security › Microphone."));

        var selected = _settings.MicrophoneDevice;
        var name = string.IsNullOrWhiteSpace(selected) ? $"System default ({devices[0]})" : selected!;
        return Task.FromResult(Ok("Microphone", name));
    }

    /// <inheritdoc />
    public async Task<ComponentHealth> CheckAiAsync(CancellationToken ct = default)
    {
        if (!_settings.AiEnabled)
            return Ok("AI enhancement", "Disabled — dictation runs in fast verbatim mode");

        bool available;
        try { available = await _ai.IsAvailableAsync(ct); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AI availability probe failed.");
            available = false;
        }

        return available
            ? Ok("AI enhancement", $"Ready — {_ai.Name}")
            : Degraded("AI enhancement", "Ollama unreachable",
                "AI cleanup is skipped until Ollama is running — dictation still works verbatim.",
                "Start Ollama (or re-toggle AI in Settings to launch it).");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ComponentHealth>> CheckAllAsync(CancellationToken ct = default)
    {
        var speech = await CheckSpeechAsync(ct);
        var mic = await CheckMicrophoneAsync(ct);
        var ai = await CheckAiAsync(ct);
        return new[] { speech, mic, ai };
    }

    private static ComponentHealth Ok(string component, string summary) =>
        new(component, HealthState.Ok, summary, Array.Empty<string>());

    private static ComponentHealth Degraded(string component, string summary, params string[] fixes) =>
        new(component, HealthState.Degraded, summary, fixes);

    private static ComponentHealth Down(string component, string summary, params string[] fixes) =>
        new(component, HealthState.Down, summary, fixes);
}
