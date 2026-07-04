namespace LocalDictation.Domain;

/// <summary>
/// The result of transcribing an <see cref="AudioClip"/>, plus any AI-enhanced text.
/// </summary>
/// <remarks>
/// <see cref="RawText"/> is the verbatim Whisper output; <see cref="ProcessedText"/>
/// is the (optionally) LLM-refined version that actually gets inserted. When no AI
/// processing runs, both are equal.
/// </remarks>
public sealed class Transcript
{
    /// <summary>Verbatim speech-to-text output.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>AI-refined text; equals <see cref="RawText"/> when no processing ran.</summary>
    public string ProcessedText { get; set; } = string.Empty;

    /// <summary>Model confidence in 0..1 (best-effort; -1 when unknown).</summary>
    public float Confidence { get; init; } = -1f;

    /// <summary>Detected/forced language (ISO code, e.g. "en").</summary>
    public string Language { get; init; } = "en";

    /// <summary>Length of the source audio.</summary>
    public TimeSpan AudioDuration { get; init; }

    /// <summary>Wall-clock time spent inside the speech engine.</summary>
    public TimeSpan TranscriptionTime { get; init; }

    /// <summary>The final text to deliver (processed if present, else raw).</summary>
    public string FinalText =>
        string.IsNullOrWhiteSpace(ProcessedText) ? RawText : ProcessedText;

    /// <summary>True when the transcript carries no usable speech.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(RawText);

    /// <summary>Real-time factor (audio seconds transcribed per wall-clock second).</summary>
    public double RealTimeFactor =>
        TranscriptionTime.TotalSeconds <= 0 ? 0 : AudioDuration.TotalSeconds / TranscriptionTime.TotalSeconds;
}
