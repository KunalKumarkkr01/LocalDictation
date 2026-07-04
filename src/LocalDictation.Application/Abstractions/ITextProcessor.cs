using LocalDictation.Domain;
using LocalDictation.Shared;

namespace LocalDictation.Application.Abstractions;

/// <summary>
/// Optionally refines a raw transcript with a local LLM (grammar, rewrite, translate…).
/// </summary>
/// <remarks>
/// The <c>NoOpTextProcessor</c> implementation lets the whole app run with zero LLM
/// installed — transcription still works, AI enhancement is simply skipped (FR-10).
/// </remarks>
public interface ITextProcessor
{
    /// <summary>Provider name (e.g. "Ollama · phi3.5").</summary>
    string Name { get; }

    /// <summary>True when a backing LLM is reachable and a model is available.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Transforms <paramref name="text"/> according to <paramref name="mode"/>.
    /// </summary>
    /// <param name="text">Raw transcript.</param>
    /// <param name="mode">Requested transformation.</param>
    /// <param name="targetLanguage">Target language for translation modes.</param>
    /// <param name="customPrompt">Prompt template for <see cref="ProcessingMode.Custom"/>.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The refined text, or a failure result (caller falls back to raw text).</returns>
    Task<Result<string>> ProcessAsync(
        string text,
        ProcessingMode mode,
        string targetLanguage = "en",
        string? customPrompt = null,
        CancellationToken ct = default);
}
