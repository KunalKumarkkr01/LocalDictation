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
- **Distribution:** ships as a **Velopack** per-user installer, published to **GitHub Releases**
  (`LocalDictation-win-Setup.exe`), with auto-update. The small installer downloads the Whisper
  model on first run (guided onboarding); the optional Ollama LLM only when AI is enabled.
- Full original design: `implementation-plan.html`. UI design mockups: `design/control-panel-design.html`,
  `design/onboarding-design.html`. **Developer docs site (GitHub Pages): `docs/index.html`** →
  https://kunalkumarkkr01.github.io/LocalDictation/. User-facing `README.md`.
- **Architecture decisions: `docs/adr/`** (14 ADRs) — read these to understand *why* things are the way they are.

---

## Solution layout (Clean Architecture)

| Project | Role |
|---|---|
| `LocalDictation.Domain` | Entities/enums: `AudioClip`, `Transcript`, `HistoryEntry`, `SpeechModelSize`, `ProcessingMode`. No dependencies. |
| `LocalDictation.Application` | Abstractions (`ISpeechEngine`, `IOllamaLifecycle`, `IAudioCaptureService`, `IOverlayController`, `ITextProcessor`, `ISpeechModelManager`, …), `AppSettings`, the `DictationPipeline`. |
| `LocalDictation.Infrastructure` | Windows/impl: `NAudioCaptureService`, `WhisperNetEngine`, `OllamaLifecycle` + `OllamaTextProcessor`, `SpeechModelManager`, Win32 interop (hotkey, SendInput, UIA), output targets, SQLite history, `AppPaths`. |
| `LocalDictation.Desktop` | WPF app: `Program.cs` (Velopack + WPF entry point), `App.xaml.cs` (DI boot), `DictationController`, views (`OverlayWindow`, `ControlPanelWindow`, `HistoryWindow`, `OnboardingWindow`), view models, `TrayHost`, `UpdateService`, `Themes/Theme.xaml` (incl. the `AppMark` DrawingImage). |
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
- **App mark / logo (ADR-0014):** a vertical off-white capsule with a 3-bar waveform cut through it
  in negative space — the same shape as the on-screen listening pill. It is the tray icon
  (`Assets/tray.ico`), the app/window icon, and a shared `AppMark` DrawingImage in `Theme.xaml` used
  in every window title bar and onboarding. (Not the microphone glyph anymore.)
- **Windows 11 Fluent-aligned.** The control panel and history window follow the Win11
  **SettingsCard** pattern: one rounded card per setting, header icon left, control right, grouped
  under bold section headers, immediate-apply (no Save button). Standard 40px title bar with
  minimize + **maximize/restore** + close caption buttons (double-click the title bar also
  maximizes; maximize fills the work area via manual bounds, not `WindowState.Maximized`, so a
  borderless transparent window doesn't cover the taskbar), Mica-style gradient background, 8px radius.
- Shared styles live in `Themes/Theme.xaml`: `Win11Card`, `GroupHeader`, `CardTitle`, `CardDesc`,
  `Switch` (Win11 toggle), `ComboBoxStyle`, `TextBoxStyle`, `GhostButton`, `CaptionButton`/`CloseCaptionButton`, thin `ScrollBar`.
- The **listening capsule** (`OverlayWindow`) is a small acrylic glass pill, bottom-center: pulsing
  dot, **frequency-reactive waveform** (13-band FFT), target app. Gold shimmer while transcribing.

---

## Build / run / test / verify

```powershell
dotnet build LocalDictation.sln -c Debug --nologo   # build (see gotcha about output paths)
dotnet test  LocalDictation.sln --nologo            # 17 tests
dotnet run --project src/LocalDictation.Evals       # WER + latency + LLM eval

# Package + publish the installer (Velopack). Publish MULTI-FILE — see gotcha below. Bump --packVersion.
dotnet publish src/LocalDictation.Desktop/LocalDictation.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish
vpk pack --packId LocalDictation --packVersion <v> --packDir .\artifacts\publish --mainExe LocalDictation.exe --icon .\src\LocalDictation.Desktop\Assets\tray.ico --outputDir .\artifacts\Releases
vpk upload github --repoUrl https://github.com/KunalKumarkkr01/LocalDictation --publish --token $(gh auth token) --outputDir .\artifacts\Releases
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
- **PUBLISH MULTI-FILE — never `PublishSingleFile`.** whisper.net loads its native `whisper.dll`/ggml
  by probing next to the exe (`runtimes/win-x64/native/`); single-file self-extraction hides it, so the
  *installed* app fails warm-up with **"Native Library not found"** → every dictation returns
  **"No speech detected"** (the dev build is unaffected — loose files). Velopack packages the folder
  into one `Setup.exe` regardless. Velopack needs `VelopackApp.Build().Run()` as the first line of
  `Program.Main`, selected via `<StartupObject>`. A Setup.exe *reinstall* drops the `models\` sibling
  (settings/history survive); real in-app auto-updates preserve it.
- **Screenshots:** the app's transparent (`AllowsTransparency`) windows don't composite above other
  apps, so on-screen capture bleeds the desktop behind. Prefer **in-process `RenderTargetBitmap`** on
  `window.Content` (measure/arrange/UpdateLayout → Render) via a temp launch-arg hook — clean, no
  bleed. If you must screen-capture, run DPI-aware (`SetProcessDPIAware()`, 200% display), match by
  owning-process id + a size filter, and only modal `ShowDialog` windows come reliably to front.
  Segoe Fluent glyphs in C# code-behind: build from `((char)0xE922).ToString()` (literal glyphs get
  stripped). To exercise dictation without a mic, play `Evals/.../fixtures/f1.wav` while recording.

Whisper models are gitignored, under `models/whisper/`. At runtime `AppPaths` resolves them under
**`%LocalAppData%\LocalDictation\models\whisper`** (also the Velopack install root, so app-data sits
beside the versioned `current\` dir), or via the repo probe / `LOCALDICTATION_MODELS` env var in dev.
Dev-installed: `ggml-base.en.bin`, `ggml-small.bin`. Build artifacts live under `artifacts/` (gitignored).

---

## Current feature state (as of 2026-07-04 — shipped as v1.0.3)

**Shipped & verified.** *UI:* monochrome theme; glass capsule with frequency-reactive waveform + gold
processing state; Win11 SettingsCard **control panel** with the AI toggle driving the **Ollama
lifecycle**; Win11-styled **history window**; Waveform Capsule app mark (tray + windows);
`[BLANK_AUDIO]` filtering; last dictation copied to the clipboard.
*Distribution:* **Velopack installer published to GitHub Releases** with background auto-update; a
five-step **first-run onboarding wizard** (mic check → model download → optional AI → hotkey);
Ollama **auto-install + model auto-pull** on AI-enable, and **boot-time Ollama start when `AiEnabled`**;
app-data under `%LocalAppData%`; **GitHub Pages docs site** (`docs/index.html`); rewritten `README.md`.
*Settings/history:* **notify-after-dictation opt-out** (`NotifyOnComplete`, gates the toast only);
**configurable history retention** (`HistoryRetentionDays`, default **30**, `0`=forever; startup
pruning that exempts favorites/pinned); **maximize/restore** on both windows; **Open History** button
in the control panel; spaced-out star/Copy/✕ actions in history rows.

**Dropped:** live text preview (tiny-model rolling preview) — reverted (ADR-0009). Don't re-add unless asked.

**Previously-known gaps now FIXED (don't re-flag):** boot-time Ollama start when `AiEnabled`; the
startup Run key (Velopack gives a stable install path, so it no longer points at a dev build).

**Deferred / genuinely open:**
- **Code-signing** — unsigned installs show a SmartScreen "More info → Run anyway" prompt (fix:
  Azure Trusted Signing ~$10/mo or an OV cert).
- Minor: the history **search drawer overlaps** slightly on open (user OK'd it); the ~163 MB
  uncompressed exe bundles whisper.net backends for all RIDs and could be trimmed.
