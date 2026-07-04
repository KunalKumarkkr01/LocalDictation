using LocalDictation.Domain;
using LocalDictation.Shared;

namespace LocalDictation.Application.Abstractions;

/// <summary>Describes an installed or installable Whisper model.</summary>
/// <param name="Size">The model size.</param>
/// <param name="FileName">On-disk ggml file name.</param>
/// <param name="Installed">Whether the file is present locally.</param>
/// <param name="SizeBytes">File size when installed.</param>
public readonly record struct SpeechModelInfo(SpeechModelSize Size, string FileName, bool Installed, long SizeBytes);

/// <summary>Manages downloading, verifying and locating Whisper model files.</summary>
public interface ISpeechModelManager
{
    /// <summary>Absolute path to the ggml file for <paramref name="size"/> (whether or not present).</summary>
    string GetModelPath(SpeechModelSize size);

    /// <summary>Whether the model file for <paramref name="size"/> exists locally.</summary>
    bool IsInstalled(SpeechModelSize size);

    /// <summary>Lists all known models and their install state.</summary>
    IReadOnlyList<SpeechModelInfo> List();

    /// <summary>Downloads and verifies a model, reporting progress in 0..1.</summary>
    Task<Result> DownloadAsync(SpeechModelSize size, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Picks a sensible default model for the current hardware (resource-aware).</summary>
    SpeechModelSize RecommendedForHardware();
}
