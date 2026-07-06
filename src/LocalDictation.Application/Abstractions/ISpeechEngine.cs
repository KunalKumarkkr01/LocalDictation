using LocalDictation.Domain;
using LocalDictation.Shared;

namespace LocalDictation.Application.Abstractions;

/// <summary>Options that tune a single transcription request.</summary>
/// <param name="Language">ISO language code, or "auto" to detect.</param>
/// <param name="Translate">When true, translate speech to English.</param>
/// <param name="InitialPrompt">Optional context/vocabulary hint biasing recognition.</param>
public readonly record struct TranscribeOptions(string Language = "auto", bool Translate = false, string? InitialPrompt = null);

/// <summary>Why the speech engine is (or isn't) ready to transcribe — drives accurate diagnostics.</summary>
public enum SpeechReadiness
{
    /// <summary>Model loaded and ready.</summary>
    Ready,
    /// <summary>No model file is installed for the active size.</summary>
    ModelNotInstalled,
    /// <summary>The native whisper/ggml library failed to load (the multi-file-publish trap).</summary>
    NativeLibraryMissing,
    /// <summary>The model file exists but could not be loaded (corrupt/unsupported).</summary>
    LoadFailed,
    /// <summary>Not yet warmed up; readiness is unknown until the first load attempt.</summary>
    NotWarmedUp
}

/// <summary>The engine's current readiness plus a human-readable detail for diagnostics.</summary>
/// <param name="State">The readiness classification.</param>
/// <param name="Detail">Optional detail (e.g. the underlying load error message).</param>
public readonly record struct SpeechEngineStatus(SpeechReadiness State, string? Detail = null);

/// <summary>
/// Transcribes captured audio into text, entirely on-device.
/// </summary>
/// <remarks>
/// Default implementation wraps Whisper.net (whisper.cpp). Alternatives (ONNX/DirectML,
/// cloud plugin) implement the same contract and are selected via DI/settings.
/// </remarks>
public interface ISpeechEngine : IAsyncDisposable
{
    /// <summary>Display name of the engine + active model.</summary>
    string Name { get; }

    /// <summary>True once the model is loaded and ready to transcribe.</summary>
    bool IsReady { get; }

    /// <summary>
    /// The engine's readiness classification and detail, refreshed on every warm-up/reload attempt.
    /// Lets callers distinguish "model missing" from "native library failed to load" from
    /// "not warmed up yet" so diagnostics and toasts can show the real reason.
    /// </summary>
    SpeechEngineStatus Status { get; }

    /// <summary>
    /// Loads the model into memory ahead of first use to eliminate cold-start latency.
    /// Safe to call repeatedly; subsequent calls are no-ops.
    /// </summary>
    Task WarmUpAsync(CancellationToken ct = default);

    /// <summary>
    /// Forces the model to be unloaded and reloaded (e.g. after installing a model or switching size).
    /// Updates <see cref="Status"/>. Used by the Settings "Reload model" action.
    /// </summary>
    Task ReloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Transcribes <paramref name="clip"/> to text.
    /// </summary>
    /// <param name="clip">16 kHz mono audio to transcribe.</param>
    /// <param name="options">Language / translation / prompt options.</param>
    /// <param name="ct">Cancellation (ESC aborts).</param>
    /// <returns>A transcript on success, or a failure result with a reason.</returns>
    Task<Result<Transcript>> TranscribeAsync(AudioClip clip, TranscribeOptions options, CancellationToken ct = default);
}
