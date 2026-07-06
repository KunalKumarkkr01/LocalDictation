# ADR-0015: Accurate failure reasons + readiness checks and a self-test

- **Status:** Accepted
- **Date:** 2026-07-06

## Context
Dictation sometimes produced no text and the notification always said **"No speech detected"** —
even when the real cause was a missing model or an unloadable native `whisper.dll`. The pipeline
computed the true reason but `DictationController` discarded `outcome.Message` and drove the toast
from a bare empty-string check, so a dead engine, a genuinely silent mic, and real silence were
indistinguishable. Warm-up failures at boot were also swallowed, so a broken engine was only
discovered after recording a doomed clip.

## Decision
- **Classify failures.** `DictationOutcome` carries a `DictationFailure` (`NoAudio`, `EngineNotReady`,
  `TranscriptionError`, `NoSpeech`, `DeliveredToEditor`). `FailureMessages` maps each to an accurate
  title/body; hard failures always notify (overriding the notify-on-complete opt-out).
- **Engine reports why.** `ISpeechEngine.Status` exposes a `SpeechEngineStatus` (`Ready`,
  `ModelNotInstalled`, `NativeLibraryMissing`, `LoadFailed`, `NotWarmedUp`); `WhisperNetEngine`
  records it on every warm-up/reload and detects native-library load failures explicitly.
- **Pre-flight.** A new `IReadinessService` checks speech, microphone and (optional) AI; the hotkey
  path blocks a doomed recording when a dependency is hard-down and shows the real reason + a fix.
- **Settings › System status.** Live health rows, a **Reload model** action (`ISpeechEngine.ReloadAsync`),
  and a **Test dictation** self-test (`IDictationSelfTest`) that synthesizes a known phrase via Windows
  TTS and runs it through the real engine — deterministic, offline and mic-free.

## Consequences
- Users see the actual cause ("Speech model not ready", "No microphone found") instead of a blanket
  "No speech detected", and a path to fix it.
- The self-test reuses the Evals TTS technique; `System.Speech` is now referenced by Infrastructure
  (Windows-only, matching the app). It reads raw headerless PCM from `SetOutputToAudioStream` directly.
- Status dots stay within the design language: off-white = ok, gold (the sanctioned processing accent)
  = degraded, danger red = down.
