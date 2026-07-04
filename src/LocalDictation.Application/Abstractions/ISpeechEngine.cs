using LocalDictation.Domain;
using LocalDictation.Shared;

namespace LocalDictation.Application.Abstractions;

/// <summary>Options that tune a single transcription request.</summary>
/// <param name="Language">ISO language code, or "auto" to detect.</param>
/// <param name="Translate">When true, translate speech to English.</param>
/// <param name="InitialPrompt">Optional context/vocabulary hint biasing recognition.</param>
public readonly record struct TranscribeOptions(string Language = "auto", bool Translate = false, string? InitialPrompt = null);

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
    /// Loads the model into memory ahead of first use to eliminate cold-start latency.
    /// Safe to call repeatedly; subsequent calls are no-ops.
    /// </summary>
    Task WarmUpAsync(CancellationToken ct = default);

    /// <summary>
    /// Transcribes <paramref name="clip"/> to text.
    /// </summary>
    /// <param name="clip">16 kHz mono audio to transcribe.</param>
    /// <param name="options">Language / translation / prompt options.</param>
    /// <param name="ct">Cancellation (ESC aborts).</param>
    /// <returns>A transcript on success, or a failure result with a reason.</returns>
    Task<Result<Transcript>> TranscribeAsync(AudioClip clip, TranscribeOptions options, CancellationToken ct = default);
}
