using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using LocalDictation.Shared;

namespace LocalDictation.Infrastructure.Ai;

/// <summary>
/// A text processor that returns the transcript unchanged. Guarantees the app keeps
/// working with zero LLM installed — transcription proceeds, AI enhancement is skipped (FR-10).
/// </summary>
public sealed class NoOpTextProcessor : ITextProcessor
{
    /// <inheritdoc />
    public string Name => "No processing";

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<Result<string>> ProcessAsync(
        string text, ProcessingMode mode, string targetLanguage = "en",
        string? customPrompt = null, CancellationToken ct = default)
        => Task.FromResult(Result<string>.Ok(text));
}
