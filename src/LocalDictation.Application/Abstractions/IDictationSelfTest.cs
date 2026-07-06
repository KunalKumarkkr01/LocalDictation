namespace LocalDictation.Application.Abstractions;

/// <summary>Outcome of an end-to-end dictation self-test.</summary>
/// <param name="Passed">True when the engine transcribed the sample closely enough.</param>
/// <param name="Heard">What the engine actually transcribed.</param>
/// <param name="Reference">The known phrase that was spoken to the engine.</param>
/// <param name="Elapsed">How long transcription took.</param>
/// <param name="Error">Failure detail when the test could not run (e.g. engine not ready); null on a clean pass/fail.</param>
public readonly record struct SelfTestResult(
    bool Passed, string Heard, string Reference, TimeSpan Elapsed, string? Error);

/// <summary>
/// Verifies the transcription path works by synthesizing a known phrase and running it through the
/// real speech engine — a deterministic, offline, mic-free health check for the Settings "Test" button.
/// </summary>
public interface IDictationSelfTest
{
    /// <summary>Runs the self-test and reports whether the engine recognised the sample phrase.</summary>
    Task<SelfTestResult> RunAsync(CancellationToken ct = default);
}
