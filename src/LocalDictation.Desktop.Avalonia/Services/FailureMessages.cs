using LocalDictation.Application.Pipeline;

namespace LocalDictation.Services;

/// <summary>
/// Maps a structured <see cref="DictationFailure"/> to an accurate, actionable notification so the
/// user sees the real reason a dictation produced no text — and how to fix it — instead of a generic
/// "No speech detected" for every cause. Port of the Windows FailureMessages.
/// </summary>
public static class FailureMessages
{
    /// <summary>A ready-to-show notification: title, body, and whether it is an error (vs. info).</summary>
    /// <param name="Title">Toast title.</param>
    /// <param name="Body">Toast body, including a fix step where one applies.</param>
    /// <param name="IsError">True for hard failures that should always notify (overriding the opt-out).</param>
    public readonly record struct Message(string Title, string Body, bool IsError);

    /// <summary>Builds the notification for an empty-text outcome.</summary>
    /// <param name="failure">The pipeline's failure classification.</param>
    /// <returns>Title/body/severity for the toast.</returns>
    public static Message For(DictationFailure failure) => failure switch
    {
        DictationFailure.EngineNotReady => new Message(
            "Speech model not ready",
            "The speech engine isn't loaded. Open Settings › System status and press Reload model (or finish onboarding to download a model).",
            IsError: true),

        DictationFailure.TranscriptionError => new Message(
            "Transcription failed",
            "Something went wrong while transcribing. Try again; if it keeps happening, reload the model in Settings › System status.",
            IsError: true),

        DictationFailure.NoAudio => new Message(
            "Nothing captured",
            "No audio was recorded. Check that the right microphone is selected in Settings and isn't muted.",
            IsError: false),

        // Genuine silence — friendly, non-error.
        _ => new Message(
            "No speech detected",
            "Try speaking a bit louder or closer to the mic.",
            IsError: false),
    };
}
