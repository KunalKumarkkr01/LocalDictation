# CLAUDE.md — LocalDictation

Project-level instructions and context for Claude Code. These take precedence over global
preferences for this repository. Read this fully at the start of every session.

---

## Commit authorship

For **this project**, commits **should include a Claude co-author trailer** (the owner has
opted in; this intentionally overrides the global "never add Claude authorship" rule).

End every commit message (and PR body) with a trailing line:

```
Co-Authored-By: Claude <noreply@anthropic.com>
```

Commit in **focused, phased commits** (one logical change each), not one big dump.

---

## What this is

LocalDictation is a Windows 11 desktop app for **system-wide, offline, AI voice dictation**:
press a global hotkey (`Ctrl+Shift+Space`), speak, transcribe locally with Whisper, optionally
enhance with a local LLM (Ollama), and insert the text into the focused control. Everything runs
on-device; nothing goes to the cloud.

- **Stack:** .NET 8, C#, WPF, Clean Architecture + MVVM.
- **Interaction:** toggle hotkey — press to start, press again to send. ESC cancels. VAD
  auto-stop is optional and **off by default** (never chops speech mid-sentence).
- **AI enhancement is opt-in (off by default)** for a fast, verbatim default.
- Full original design: `implementation-plan.html`. UI design mockup: `design/control-panel-design.html`.
- **Architecture decisions: `docs/adr/`** — read these to understand *why* things are the way they are.

---

## Solution layout (Clean Architecture)

| Project | Role |
|---|---|
| `LocalDictation.Domain` | Entities/enums: `AudioClip`, `Transcript`, `HistoryEntry`, `SpeechModelSize`, `ProcessingMode`. No dependencies. |
| `LocalDictation.Application` | Abstractions (`ISpeechEngine`, `IOllamaLifecycle`, `IAudioCaptureService`, `IOverlayController`, `ITextProcessor`, `ISpeechModelManager`, …), `AppSettings`, the `DictationPipeline`. |
| `LocalDictation.Infrastructure` | Windows/impl: `NAudioCaptureService`, `WhisperNetEngine`, `OllamaLifecycle` + `OllamaTextProcessor`, `SpeechModelManager`, Win32 interop (hotkey, SendInput, UIA), output targets, SQLite history, `AppPaths`. |
| `LocalDictation.Desktop` | WPF app: `App.xaml.cs` (DI boot), `DictationController`, views (`OverlayWindow`, `ControlPanelWindow`, `HistoryWindow`), view models, `TrayHost`, `Themes/Theme.xaml`. |
| `LocalDictation.Evals` | Offline WER/latency/LLM evaluation harness with fixtures. |
| `*.Tests` | Unit + NetArchTest architecture tests (17 total). |

**Key flow:** hotkey → `DictationController` inspects the focused target (privacy blocks) → shows
the capsule overlay → `NAudioCaptureService` captures 16 kHz mono → on stop, `DictationPipeline`
runs `WhisperNetEngine` (+ optional `OllamaTextProcessor`) → `OutputRouter` inserts via
clipboard/SendInput/UIA → the final text is also left on the clipboard for re-pasting.

---

## Design language (see ADR-0004, ADR-0005)

- **Monochrome black-and-white.** Near-black grounds (`#0A0A0B`/`#141316`), warm off-white
  (`#F4F2EF`), white as the only accent. **No violet, no color** — except one deliberate
  exception: the capsule's transient **processing state uses a soft gold** (`#E8B478`, ADR-0008).
- **Windows 11 Fluent-aligned.** The control panel and history window follow the Win11
  **SettingsCard** pattern: one rounded card per setting, header icon left, control right, grouped
  under bold section headers, immediate-apply (no Save button). Standard 40px title bar with
  minimize + close caption buttons, Mica-style gradient background, 8px window radius.
- Shared styles live in `Themes/Theme.xaml`: `Win11Card`, `GroupHeader`, `CardTitle`, `CardDesc`,
  `Switch` (Win11 toggle), `ComboBoxStyle`, `CaptionButton`/`CloseCaptionButton`, thin `ScrollBar`.
- The **listening capsule** (`OverlayWindow`) is a small acrylic glass pill, bottom-center: pulsing
  dot, **frequency-reactive waveform** (13-band FFT), target app. Gold shimmer while transcribing.

---

## Build / run / test / verify

```powershell
dotnet build LocalDictation.sln -c Debug --nologo   # build (see gotcha about output paths)
dotnet test  LocalDictation.sln --nologo            # 17 tests
dotnet run --project src/LocalDictation.Evals       # WER + latency + LLM eval
```

**Critical build/test gotchas** (also in memory `localdictation-build-env`):
- **`global.json` pins SDK 8.0.417.** The machine's default SDK is 10, which emits `.slnx` that
  8.0 MSBuild can't parse. Keep the classic `.sln`.
- **Stale-exe trap:** `dotnet build LocalDictation.sln` writes the Desktop exe to
  `src/LocalDictation.Desktop/bin/**x64**/Debug/net8.0-windows/`, but building the **csproj**
  directly writes to `bin/Debug/net8.0-windows/`. Launching the wrong one runs stale code. When
  verifying a change: **clean-rebuild the Desktop project** (`Remove-Item obj,bin`) and launch the
  freshly built exe. Incremental WPF builds also leave stale binaries.
- WPF markup compiler + CommunityToolkit.Mvvm `[ObservableProperty]`/`[RelayCommand]` generators
  collide (CS0102 in `*_wpftmp`). **View models use hand-written `SetProperty` properties.**
- **Screenshots must run DPI-aware** (`SetProcessDPIAware()`) — the display is 200% DPI. To capture
  a window: enumerate the app's top-level windows, `MoveWindow` it on-screen, force-foreground via
  `AttachThreadInput`. To exercise dictation without a mic, play `Evals/.../fixtures/f1.wav` while recording.

Whisper models are gitignored, under `models/whisper/` (resolved via `AppPaths` probe or
`LOCALDICTATION_MODELS`). Installed: `ggml-base.en.bin`, `ggml-small.bin`.

---

## Current feature state (as of 2026-07-04)

Shipped & verified: monochrome theme; glass capsule with frequency-reactive waveform + gold
processing state; Win11 SettingsCard **control panel** (replaces old Settings) with the AI toggle
driving the **Ollama lifecycle**; Win11-styled **history window**; caption buttons; thin scrollbar;
monochrome mic tray icon; `[BLANK_AUDIO]` filtering; **last dictation copied to the clipboard**.

**Dropped:** live text preview (tiny-model rolling preview) — reverted (ADR-0009). Don't re-add unless asked.

**Known gaps:**
- On reboot the **app + Whisper model auto-load** (Run key + `WarmUpAsync`), but **Ollama/LLM do
  not** — AI is off by default, and even when on, Ollama is only started when the control panel
  opens, not at boot. Wiring boot-time Ollama start when `AiEnabled` is a proposed change.
- The startup Run key points at the **dev build path** (`bin/x64/Debug/...`); a published build in
  a stable location is the eventual fix.
