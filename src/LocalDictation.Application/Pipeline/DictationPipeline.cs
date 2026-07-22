using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Shared;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Application.Pipeline;

/// <summary>Classifies why a dictation ended, so the UI can show the real reason and a fix step.</summary>
public enum DictationFailure
{
    /// <summary>Success — text was delivered to the target.</summary>
    None,
    /// <summary>No audio was captured (empty/too-short clip).</summary>
    NoAudio,
    /// <summary>The speech engine is not loaded (model missing or native library failed to load).</summary>
    EngineNotReady,
    /// <summary>The engine loaded but transcription itself errored.</summary>
    TranscriptionError,
    /// <summary>Audio was captured but contained no recognisable speech.</summary>
    NoSpeech,
    /// <summary>Text was routed to the floating editor rather than auto-inserted (not an error).</summary>
    DeliveredToEditor
}

/// <summary>Terminal outcome of running the dictation pipeline for one session.</summary>
/// <param name="Session">The session with its final state and transcript.</param>
/// <param name="Delivered">Whether text reached the target (vs. floating editor / cancelled).</param>
/// <param name="Message">Human-readable status for the overlay/toast.</param>
/// <param name="Failure">Structured reason the dictation ended, for accurate notifications.</param>
/// <param name="Oversized">True when the transcript likely exceeded the LLM context budget and may have been truncated.</param>
public readonly record struct DictationOutcome(
    DictationSession Session, bool Delivered, string Message,
    DictationFailure Failure = DictationFailure.None, bool Oversized = false);

/// <summary>
/// Orchestrates a single dictation from captured audio to delivered text, coordinating
/// the speech engine, optional AI processor, output router and history — all off the UI thread.
/// </summary>
/// <remarks>
/// This is the heart of the Application layer. It depends only on ports, so every stage
/// is independently replaceable and unit-testable with mocks. Cancellation (ESC) is honoured
/// end-to-end via the supplied token.
/// </remarks>
public sealed class DictationPipeline
{
    private readonly ISpeechEngine _speech;
    private readonly ITextProcessor _processor;
    private readonly IOutputRouter _router;
    private readonly IHistoryRepository _history;
    private readonly IPersonaResolver _resolver;
    private readonly PersonaSettings _personas;
    private readonly ILogger<DictationPipeline> _log;

    /// <summary>Creates the pipeline from its collaborating ports.</summary>
    public DictationPipeline(
        ISpeechEngine speech,
        ITextProcessor processor,
        IOutputRouter router,
        IHistoryRepository history,
        IPersonaResolver resolver,
        PersonaSettings personas,
        ILogger<DictationPipeline> log)
    {
        _speech = speech;
        _processor = processor;
        _router = router;
        _history = history;
        _resolver = resolver;
        _personas = personas;
        _log = log;
    }

    /// <summary>
    /// Runs transcription → optional AI processing → delivery → history for a captured clip.
    /// </summary>
    /// <param name="clip">The recorded audio.</param>
    /// <param name="target">The window/control captured at trigger time.</param>
    /// <param name="settings">Active settings (mode, language, AI toggle…).</param>
    /// <param name="ct">Cancellation token (ESC).</param>
    /// <param name="personaOverride">An explicit picker choice; wins over auto-detection.</param>
    /// <returns>The session outcome; never throws for expected failures.</returns>
    public async Task<DictationOutcome> RunAsync(
        AudioClip clip, TargetControl target, AppSettings settings, CancellationToken ct, Persona? personaOverride = null)
    {
        var decision = _resolver.Decide(target, settings, _personas, personaOverride);
        var mode = decision.Enhance ? decision.Mode : ProcessingMode.None;
        var session = new DictationSession(target, mode);

        if (clip.IsEmpty)
        {
            session.Transition(SessionState.Cancelled);
            return new DictationOutcome(session, false, "No audio captured.", DictationFailure.NoAudio);
        }

        // --- 1. Transcribe ---
        session.Transition(SessionState.Transcribing);
        var options = new TranscribeOptions(settings.Language, Translate: false);
        Result<Transcript> asr = await _speech.TranscribeAsync(clip, options, ct);
        if (ct.IsCancellationRequested) return Cancel(session);
        if (!asr.IsSuccess)
        {
            session.Transition(SessionState.Failed);
            _log.LogWarning("Transcription failed: {Error}", asr.Error);
            // Distinguish "engine couldn't load" from "engine ran but errored" so the toast can
            // point the user at Settings › System status (reload) vs. a generic retry.
            var kind = _speech.Status.State == SpeechReadiness.Ready
                ? DictationFailure.TranscriptionError
                : DictationFailure.EngineNotReady;
            return new DictationOutcome(session, false, $"Transcription failed: {asr.Error}", kind);
        }

        var transcript = asr.Value!;
        if (transcript.IsEmpty)
        {
            session.Transition(SessionState.Cancelled);
            return new DictationOutcome(session, false, "No speech detected.", DictationFailure.NoSpeech);
        }
        transcript.ProcessedText = transcript.RawText;
        session.AttachTranscript(transcript);

        // --- 2. Optional AI processing (degrades gracefully to raw text) ---
        var oversized = false;
        if (decision.Enhance && mode != ProcessingMode.None)
        {
            // Pre-flight: a transcript far larger than the context budget will be silently truncated
            // by Ollama. Flag it so the caller can warn; enhancement still runs and raw is preserved.
            var budget = settings.LlmContextTokens > 0 ? settings.LlmContextTokens : 4096;
            oversized = transcript.RawText.Length / 4 > budget * 0.75;
            if (oversized)
                _log.LogWarning("Transcript ~{Tokens} tokens exceeds safe budget ({Budget}); may truncate.",
                    transcript.RawText.Length / 4, budget);

            session.Transition(SessionState.Processing);
            var processed = await ProcessSafelyAsync(transcript.RawText, mode, settings, decision.SystemPrompt, ct);
            if (ct.IsCancellationRequested) return Cancel(session);
            transcript.ProcessedText = processed;
        }

        // --- 3. Deliver ---
        session.Transition(SessionState.Delivering);
        OutputResult delivery = await _router.RouteAsync(transcript.FinalText, target, ct);

        // --- 4. History (best-effort, never blocks the result) ---
        if (settings.HistoryEnabled)
            await SaveHistorySafelyAsync(session, transcript, ct);

        session.Transition(delivery.Success ? SessionState.Completed : SessionState.Failed);
        return new DictationOutcome(
            session,
            delivery.Success,
            delivery.Success ? $"Inserted via {delivery.StrategyUsed}." : "Opened floating editor.",
            delivery.Success ? DictationFailure.None : DictationFailure.DeliveredToEditor,
            oversized);
    }

    /// <summary>Runs AI processing but falls back to the raw text on any failure (FR-10).</summary>
    private async Task<string> ProcessSafelyAsync(string raw, ProcessingMode mode, AppSettings settings, string? systemPromptOverride, CancellationToken ct)
    {
        try
        {
            if (!await _processor.IsAvailableAsync(ct)) return raw;
            var result = await _processor.ProcessAsync(raw, mode, settings.TranslationTarget, settings.CustomPrompt, systemPromptOverride, ct);
            return result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value) ? result.Value! : raw;
        }
        // Only a genuine user cancellation propagates; an HTTP timeout degrades to raw text.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AI processing failed; using raw transcript.");
            return raw;
        }
    }

    /// <summary>Persists a history entry without letting storage errors affect delivery.</summary>
    private async Task SaveHistorySafelyAsync(DictationSession session, Transcript transcript, CancellationToken ct)
    {
        try
        {
            await _history.AddAsync(new HistoryEntry
            {
                Id = session.Id,
                App = session.Target.ProcessName,
                RawText = transcript.RawText,
                ProcessedText = transcript.FinalText,
                Mode = session.Mode,
                Language = transcript.Language,
                DurationMs = (long)transcript.AudioDuration.TotalMilliseconds
            }, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist history entry.");
        }
    }

    private static DictationOutcome Cancel(DictationSession s)
    {
        s.Transition(SessionState.Cancelled);
        return new DictationOutcome(s, false, "Cancelled.");
    }
}
