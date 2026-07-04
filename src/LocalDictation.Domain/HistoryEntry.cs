namespace LocalDictation.Domain;

/// <summary>
/// A persisted record of a completed dictation, shown in the searchable history view.
/// </summary>
public sealed class HistoryEntry
{
    /// <summary>Primary key (also the originating session id).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>When the dictation completed.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Target application process name.</summary>
    public string App { get; init; } = string.Empty;

    /// <summary>Verbatim transcript.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>Delivered (possibly AI-processed) text.</summary>
    public string ProcessedText { get; init; } = string.Empty;

    /// <summary>Processing mode used.</summary>
    public ProcessingMode Mode { get; init; }

    /// <summary>Transcript language.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Source audio length in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>User-flagged favourite.</summary>
    public bool Favorite { get; set; }

    /// <summary>Pinned entries are exempt from retention pruning.</summary>
    public bool Pinned { get; set; }
}
