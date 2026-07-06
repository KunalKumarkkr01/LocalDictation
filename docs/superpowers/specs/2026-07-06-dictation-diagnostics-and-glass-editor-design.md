# Dictation reliability, diagnostics & glass fallback editor — Design

**Date:** 2026-07-06
**Status:** Implemented — all phases built, tests green (17/17), self-test verified PASS end-to-end
**Owner:** Kunal

---

## 1. Problem & root cause

Users report that dictation "sometimes doesn't work" and the notification shows the **wrong reason** —
typically "No speech detected" even when the real cause is a missing/unloadable model.

**Root cause:** `DictationController.FinishAsync` derives the user-facing toast from a bare
empty-string check on the heard text, and **discards the pipeline's structured `outcome.Message`**
(logged only to `StartupLog`, `DictationController.cs:165`). Every distinct empty-result cause
collapses into one misleading toast:

| Real cause | Reason the pipeline/engine produces | What the user sees today |
|---|---|---|
| Whisper model not installed | "Transcription failed: Whisper model unavailable. Download a model in Settings." | **"No speech detected."** |
| Native `whisper.dll`/ggml failed to load (the single-file trap) | "Transcription failed: <native load error>" | **"No speech detected."** |
| Whisper threw mid-decode | real exception message | **"No speech detected."** |
| Mic captured genuine silence | "No speech detected." | "No speech detected." (correct) |

Compounding factors:
- **Boot warm-up failures are swallowed** (`DictationController.cs:69-70`) — a broken engine is only
  discovered after the user records and gets a dead-end toast.
- There is **no readiness/pre-flight check**: the app happily records into a doomed pipeline.
- The engine has **no dedicated "native library missing" signal**; that failure surfaces as a raw
  exception string and is then dropped.

## 2. Goals (verifiable)

1. **Right reason, every time.** When dictation fails, the toast names the *actual* cause and gives a
   fix step. Verify: with the model file removed, a dictation attempt shows "Speech model not ready"
   (not "No speech detected") with a "reload in Settings" step.
2. **Pre-flight readiness.** Pressing the hotkey when the engine or mic is down shows a specific,
   actionable notification **before** recording a doomed clip. Verify: no mic → "No microphone found"
   with steps; model missing → "Speech model not ready" with steps; neither records audio.
3. **Settings › System status.** A status section shows live health of Speech engine, Microphone, and
   AI (when enabled); a **Reload model** action; and a **Test dictation** self-test button.
   Verify: reload after fixing a model flips status Down→Ready; self-test on a healthy engine reports
   PASS with the recognized phrase.
4. **Glass fallback editor on focus-loss.** If the originally targeted window is no longer foreground
   at delivery time, the dictated text opens in a translucent dark-glass editor (min/max/restore/close
   chrome) instead of being typed into the wrong app. Verify: dictate into Notepad, alt-tab to another
   window before stopping → the editor appears with the text, nothing is typed into the other window.

## 3. Non-goals (YAGNI)

- No re-insert-from-editor button (clipboard already carries the last dictation; manual paste covers it).
- No audible playback / mic-loopback self-test (chosen: silent STT self-test only).
- No change to the insertion strategy chain, VAD, or AI degradation behavior.
- No control-level focus tracking — window-level foreground comparison is sufficient and robust.
- No new telemetry, no code-signing (unrelated open items).

---

## 4. Feature 1 — Structured failure reasons

### 4.1 Add a failure classification to the pipeline outcome
`DictationOutcome` gains a `DictationFailure Failure` field (default `None`). New enum in the
Application layer:

```
public enum DictationFailure { None, NoAudio, EngineNotReady, TranscriptionError, NoSpeech, DeliveredToEditor }
```

`DictationPipeline.RunAsync` sets it at each existing branch:
- empty clip → `NoAudio`
- `!asr.IsSuccess` → `TranscriptionError` (or `EngineNotReady` when `asr.Error` is the engine's
  "model unavailable / native library" sentinel — see 4.2)
- empty transcript → `NoSpeech`
- delivery routed to editor → `DeliveredToEditor` (not a failure; informational)

### 4.2 Engine reports *why* it isn't ready
`ISpeechEngine` gains a readiness descriptor so callers can distinguish "model file absent" from
"native library failed to load" from "not warmed up yet":

```
public enum SpeechReadiness { Ready, ModelNotInstalled, NativeLibraryMissing, LoadFailed, NotWarmedUp }
public readonly record struct SpeechEngineStatus(SpeechReadiness State, string? Detail);
```

`WhisperNetEngine`:
- `WarmUpAsync` no longer swallows the model-missing/native-lib cases silently. It records the outcome
  in a cached `SpeechEngineStatus` field (thread-safe under the existing `_gate`).
- Detect native-library failure by catching the specific load exception types
  (`DllNotFoundException`, `BadImageFormatException`, and Whisper.net's "native library not found"
  message) around `WhisperFactory.FromPath`/`builder.Build`, mapping to `NativeLibraryMissing`.
- Expose `SpeechEngineStatus Status { get; }` and keep `IsReady` as `Status.State == Ready`.
- `TranscribeAsync` returns `Result.Fail` carrying the same structured detail so the pipeline can map
  to `EngineNotReady`.

### 4.3 Controller surfaces the real reason
`DictationController.FinishAsync`: when the result is not delivered and text is empty, map
`outcome.Failure` → `(title, body, steps)` via a small `FailureMessages` helper and call
`_notify.Error`/`.Info`. `NoSpeech` stays a friendly `Info`; `EngineNotReady`/`TranscriptionError`
become `Error` with fix steps (e.g. "Open Settings › System status and press Reload model").
`DeliveredToEditor` shows a quiet "Opened editor" info. The `NotifyOnComplete` opt-out still gates the
success/neutral toasts, but **hard failures are always shown** (a silent failure is the bug we're
fixing).

## 5. Feature 2 — Readiness service & pre-flight

### 5.1 New port: `IReadinessService` (Application)
```
public enum HealthState { Ok, Degraded, Down }
public readonly record struct ComponentHealth(string Component, HealthState State, string Summary, IReadOnlyList<string> Fixes);

public interface IReadinessService
{
    Task<ComponentHealth> CheckSpeechAsync(CancellationToken ct = default);   // uses ISpeechEngine.Status (+ model manager)
    Task<ComponentHealth> CheckMicrophoneAsync(CancellationToken ct = default); // uses IAudioCaptureService device list
    Task<ComponentHealth> CheckAiAsync(CancellationToken ct = default);        // uses ITextProcessor/IOllamaLifecycle; Ok-N/A when AiEnabled=false
    Task<IReadOnlyList<ComponentHealth>> CheckAllAsync(CancellationToken ct = default);
}
```
Implementation `ReadinessService` (Infrastructure) composes existing services. Checks are cheap:
- **Speech:** `ISpeechEngine.Status` + `ISpeechModelManager.IsInstalled(active)`. States: model absent →
  Down ("Download a model in onboarding / Settings"); native lib missing → Down ("Reinstall the app —
  the speech library didn't load"); not warmed → Degraded ("Press Reload model"); ready → Ok.
- **Microphone:** `IAudioCaptureService.GetInputDevices()` empty → Down ("Connect a microphone / check
  Windows sound settings / grant mic permission"); else Ok (names the selected/default device).
- **AI:** when `AiEnabled` false → Ok with "Disabled" note; when true, probe
  `ITextProcessor.IsAvailableAsync` → Degraded if down ("AI enhancement is off until Ollama starts;
  dictation still works in verbatim mode").

### 5.2 Pre-flight in `StartAsync`
Keep starting the mic first (no clipped words). Then, using **cached** readiness (no synchronous model
load on the hotkey path):
- If Speech is Down → cancel capture, show the specific Error toast with fixes, abort.
- If Microphone is Down → show Error toast with fixes, abort.
- AI Degraded is **not** a blocker (pipeline degrades to raw text); surfaced only in the status panel.

Because warm-up now records status at boot, the common "native lib / missing model" case is known
immediately and blocks the doomed recording with the correct message.

## 6. Feature 3 — Settings › System status

New **"System status"** section at the top of `ControlPanelWindow`, following the existing
`Win11Card` SettingsCard pattern, driven by `ControlPanelViewModel` (hand-written `SetProperty`
properties, per the project's generator gotcha).

**Cards:**
1. **Speech engine** — status dot + text (model name + Ready/Down/Degraded), **Reload model** button.
2. **Microphone** — status dot + selected device name/health.
3. **AI (Ollama)** — status dot + Enabled/Disabled/Down; only meaningful when `AiEnabled`.
4. **Test dictation** — a **Run self-test** button + result line (PASS/FAIL, recognized phrase, latency).
5. A **Refresh** affordance (re-runs `CheckAllAsync`); status also refreshes on window open and after
   reload/self-test.

**Status dot colors (within the monochrome + sanctioned-gold rule):** Ok = off-white
(`TextPrimaryBrush` `#F4F2EF`), Degraded = gold (`ProcessingBrush` `#E8B478`), Down = danger
(`DangerBrush` `#CF9A96`). No new palette.

**Reload model:** new `Task ReloadAsync(CancellationToken)` on `ISpeechEngine` — invalidates the cached
factory and re-runs warm-up, updating `Status`. VM shows a transient "Reloading…" state and refreshes
health after.

**Self-test:** new port `IDictationSelfTest` (Application) + `TtsDictationSelfTest` (Infrastructure):
1. Synthesize a known phrase ("The quick brown fox jumps over the lazy dog.") to 16 kHz mono via
   Windows TTS (`System.Speech.Synthesis`, same technique as `Evals/SpeechFixtures.cs`) into memory.
2. Load to `AudioClip`.
3. Ensure engine ready (`ReloadAsync`/`WarmUpAsync`); if not, return FAIL with the readiness reason.
4. Run the real `TranscribeAsync`; compare normalized output to the reference by word overlap
   (PASS if ≥ ~60% of reference words present).
5. Return `SelfTestResult(bool Passed, string Heard, string Reference, TimeSpan Elapsed, string? Error)`.

> ⚠️ Dependency: `System.Speech` (already referenced by `LocalDictation.Evals`) would be added to
> `LocalDictation.Infrastructure`. It is a Windows-only assembly; the app is Windows-only WPF, so this
> is safe. Flagged per project convention. Alternative (if we want zero new deps): bundle a small WAV
> asset instead of synthesizing — not chosen, keeps the corpus self-contained like Evals.

## 7. Feature 4 — Glass fallback editor on focus-loss

### 7.1 Trigger: foreground window changed at delivery
In `OutputRouter.RouteAsync`, after the sensitive/elevated checks and **before** attempting insertion,
compare the live foreground window to the captured `target.WindowHandle`:
- Use `NativeMethods.GetForegroundWindow()` (already in interop, used by `Win32Inspector`).
- If it differs from `target.WindowHandle` (and the target handle is non-zero) → the original field is
  no longer focused → `ShowEditor(text, target, EditorReason.FocusMoved)` and return
  `OutputResult.Failed("editor", "Focus moved.")`. Do **not** iterate insertion targets.
- Existing fallbacks remain: sensitive → `EditorReason.Sensitive`, elevated → `EditorReason.Elevated`,
  all strategies failed → `EditorReason.InsertFailed`.

`IFloatingEditor.ShowFor` gains an `EditorReason` argument (new enum in Application) so the window shows
an accurate sub-label; the router passes it.

### 7.2 Redesign `FloatingEditorWindow` as translucent glass with full chrome
Rework the existing window in place (it is already the `IFloatingEditor` singleton — no parallel class):
- **Shell:** `WindowStyle="None" AllowsTransparency="True" Background="Transparent"`,
  `ResizeMode="CanResizeWithGrip"`, `ShowInTaskbar="True"`, `FontFamily="{StaticResource FontUi}"`.
  Drop `Topmost` fixed-true → make it a normal, taskbar-present window the user can manage.
- **Glass ground:** rounded root `Border` (CornerRadius 8, `BorderBrush`) using the pill's translucent
  recipe — a semi-transparent near-black fill (`#C0121214`, matching `OverlayWindow`) so it reads as
  dark glass, plus a soft `DropShadowEffect`.
- **Chrome:** copy the 40px custom title bar from `ControlPanelWindow` — `AppMark` + "Dictation" title +
  `CaptionButton` (minimize `E921`, maximize/restore `E922`/`E923`) + `CloseCaptionButton` (`E8BB`),
  plus the whole code-behind pattern: `OnDrag` (double-click maximizes), `OnMinimize`, `ToggleMaximize`
  (manual `WorkArea` bounds — the taskbar-safe trick), `OnClose` → `Hide()`.
- **Body:** a large editable `TextBox` (`TextBoxStyle`, AcceptsReturn, wrap, scroll, grows with the
  window), a reason sub-label ("focus moved" / "sensitive field" / "elevated window" / "couldn't
  auto-insert"), and actions: **Copy** (`PrimaryButton`) + **Close** (`GhostButton`).
- On show: populate text, focus + select-all, reset the Copy button label (existing behavior kept).

### 7.3 Setting (optional, small)
Add `AppSettings.EditorOnFocusLoss` (bool, default **true**) in the Output group so power users can
disable the focus-guard if they rely on cross-window insertion. Surfaced as a `Switch` card in the
control panel's Output/General area. (Include only if trivial; the guard is on by default.)

---

## 8. Architecture summary

**New/changed ports (Application):**
- `DictationFailure` enum; `DictationOutcome.Failure` field.
- `SpeechReadiness` enum, `SpeechEngineStatus` record; `ISpeechEngine.Status`, `.ReloadAsync(...)`.
- `IReadinessService` + `ComponentHealth`/`HealthState`.
- `IDictationSelfTest` + `SelfTestResult`.
- `EditorReason` enum; `IFloatingEditor.ShowFor(text, target, reason)`.
- `AppSettings.EditorOnFocusLoss` (optional).

**New/changed impls (Infrastructure):**
- `WhisperNetEngine`: status tracking, native-lib detection, `ReloadAsync`.
- `ReadinessService`.
- `TtsDictationSelfTest`.
- `OutputRouter`: foreground-changed guard.

**Desktop:**
- `DictationController`: pre-flight readiness gate; surface `outcome.Failure` via `FailureMessages`.
- `ControlPanelWindow` + `ControlPanelViewModel`: System status section, reload, self-test, refresh.
- `FloatingEditorWindow`: glass + full chrome redesign.
- DI registration for the two new services.

## 9. Data flow (happy + failure)

```
Hotkey ─► StartAsync
           ├─ mic.Start()                     (first, no clipped words)
           ├─ readiness (cached): Speech/Mic Down? ─► specific Error toast + abort
           └─ capture target (hwnd), show overlay
Hotkey ─► FinishAsync ─► pipeline.RunAsync
           ├─ transcribe (engine.Status feeds EngineNotReady mapping)
           ├─ deliver ─► OutputRouter
           │              ├─ foreground != captured hwnd ─► glass editor (FocusMoved)
           │              ├─ sensitive/elevated ─► glass editor
           │              └─ strategies ─► insert; all fail ─► glass editor
           └─ outcome.Failure ─► FailureMessages ─► correct toast
```

## 10. Error handling

- Consistent with existing patterns: expected failures return `Result`/`OutputResult`, not exceptions;
  the pipeline never throws for expected cases.
- Readiness checks are defensive (device enumeration and Ollama probes already swallow/return safely).
- Self-test wraps TTS + transcription in try/catch and returns a FAIL result rather than throwing.
- Hard failures (engine/mic down) are **always** notified, overriding `NotifyOnComplete` (that opt-out
  is for success toasts only).

## 11. Testing

- Unit (Application): `DictationPipeline` sets the correct `DictationFailure` per branch (extend
  existing `DictationPipelineTests`). `FailureMessages` maps every enum value to non-empty title/steps.
- Unit: `ReadinessService` maps engine/mic/AI states to the expected `HealthState` using fakes.
- Existing 17 tests must stay green; NetArchTest boundaries respected (no Infrastructure leak into
  Application; new ports live in Application).
- Manual verify (per goal): model-removed reason; no-mic pre-flight; reload flips status; self-test
  PASS; alt-tab-before-stop opens the glass editor and types nothing into the wrong window.
  `🧪 Test suggested: OutputRouter foreground-changed guard (fake foreground probe) routes to editor.`

## 12. Phasing (focused commits)

1. **Structured failure reasons** — enum + outcome field + pipeline mapping + controller
   `FailureMessages`; engine `SpeechEngineStatus` + native-lib detection. (Fixes the reported bug.)
2. **Readiness service + pre-flight gate** — `IReadinessService`/impl, DI, `StartAsync` gate.
3. **Settings › System status** — status cards, Reload model (`ISpeechEngine.ReloadAsync`), refresh.
4. **Self-test** — `IDictationSelfTest`/`TtsDictationSelfTest`, Test button + result UI.
5. **Glass fallback editor** — `OutputRouter` focus guard + `EditorReason`; `FloatingEditorWindow`
   glass/chrome redesign; optional `EditorOnFocusLoss` setting.
6. **Docs** — ADR(s) for the failure-reason model, readiness/self-test, and the focus-loss editor;
   update `docs/index.html` flow + README as needed.
