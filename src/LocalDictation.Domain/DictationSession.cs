namespace LocalDictation.Domain;

/// <summary>
/// Aggregate root representing one press-to-dictate interaction, from hotkey to delivery.
/// </summary>
/// <remarks>
/// Tracks the target, chosen processing mode, state transitions and the resulting
/// transcript. The orchestration pipeline mutates <see cref="State"/> as work progresses.
/// </remarks>
public sealed class DictationSession
{
    /// <summary>Stable identifier for correlation and history.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>When the session began.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    /// <summary>The window/control captured at trigger time.</summary>
    public TargetControl Target { get; }

    /// <summary>The AI post-processing mode selected for this session.</summary>
    public ProcessingMode Mode { get; }

    /// <summary>Current lifecycle state.</summary>
    public SessionState State { get; private set; } = SessionState.Idle;

    /// <summary>The produced transcript, once transcription completes.</summary>
    public Transcript? Transcript { get; private set; }

    /// <summary>Creates a session for a target and mode.</summary>
    public DictationSession(TargetControl target, ProcessingMode mode)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Mode = mode;
    }

    /// <summary>Advances the session to a new lifecycle state.</summary>
    public void Transition(SessionState state) => State = state;

    /// <summary>Attaches the transcript produced by the speech engine.</summary>
    public void AttachTranscript(Transcript transcript) => Transcript = transcript;
}
