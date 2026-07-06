namespace LocalDictation.Application.Abstractions;

/// <summary>Overall health of a single dictation dependency.</summary>
public enum HealthState
{
    /// <summary>Working / available.</summary>
    Ok,
    /// <summary>Usable but impaired — dictation still works, a feature is off (e.g. AI unavailable).</summary>
    Degraded,
    /// <summary>Broken — dictation cannot work until fixed (e.g. no model, no mic).</summary>
    Down
}

/// <summary>A health snapshot for one component, with fix steps when it isn't Ok.</summary>
/// <param name="Component">Display name (e.g. "Speech engine", "Microphone", "AI enhancement").</param>
/// <param name="State">Health classification.</param>
/// <param name="Summary">One-line status (e.g. "Ready — base.en" / "No microphone found").</param>
/// <param name="Fixes">Ordered, user-actionable steps to resolve a non-Ok state (empty when Ok).</param>
public readonly record struct ComponentHealth(
    string Component, HealthState State, string Summary, IReadOnlyList<string> Fixes);

/// <summary>
/// Checks that the services dictation depends on (speech engine, microphone, optional AI) are up,
/// so the app can pre-flight before recording and show accurate status + fixes in Settings.
/// </summary>
/// <remarks>
/// Checks are intentionally cheap (cached engine status, device enumeration, a quick AI probe) so
/// they can run on the hotkey path without adding perceptible latency.
/// </remarks>
public interface IReadinessService
{
    /// <summary>Health of the speech engine (model installed + native library loaded + warmed).</summary>
    Task<ComponentHealth> CheckSpeechAsync(CancellationToken ct = default);

    /// <summary>Health of the microphone (at least one capture device present).</summary>
    Task<ComponentHealth> CheckMicrophoneAsync(CancellationToken ct = default);

    /// <summary>Health of AI enhancement (Ok/Disabled when off; Degraded when on but unreachable).</summary>
    Task<ComponentHealth> CheckAiAsync(CancellationToken ct = default);

    /// <summary>All component checks, in display order (speech, microphone, AI).</summary>
    Task<IReadOnlyList<ComponentHealth>> CheckAllAsync(CancellationToken ct = default);
}
