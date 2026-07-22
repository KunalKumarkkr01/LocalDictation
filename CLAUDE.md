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

LocalDictation is a **Windows 11 + macOS** desktop app for **system-wide, offline, AI voice
dictation**: press a global hotkey (`Ctrl+Shift+Space`), speak, transcribe locally with Whisper,
optionally enhance with a local LLM (Ollama), and insert the text into the focused control.
Everything runs on-device; nothing goes to the cloud.

- **Stack:** .NET 8, C#, Clean Architecture + MVVM. **WPF** on Windows, **Avalonia** on macOS,
  sharing one portable core (Domain/Application/Shared/the base Infrastructure project).
- **Interaction:** toggle hotkey — press to start, press again to send. ESC cancels. VAD
  auto-stop is optional and **off by default** (never chops speech mid-sentence).
- **AI enhancement is opt-in (off by default)** for a fast, verbatim default.
- **Distribution:** Windows ships as a **Velopack** per-user installer (`LocalDictation-win-Setup.exe`)
  with auto-update; macOS ships as a **`.dmg`** (`LocalDictation-osx-Setup.dmg` arm64,
  `LocalDictation-osx-x64-Setup.dmg` x64) built by `release-macos.yml` — **currently unsigned**
  (Gatekeeper right-click→Open needed; codesign/notarize steps exist but are no-ops until Apple
  Developer secrets are added, see "Deferred" below), no auto-update yet. Both platforms download
  the Whisper model on first run (guided onboarding); the optional Ollama LLM only when AI is enabled.
- Full original design: `implementation-plan.html`. UI design mockups: `design/control-panel-design.html`,
  `design/onboarding-design.html`. **Developer docs site (GitHub Pages): `docs/index.html`** →
  https://kunalkumarkkr01.github.io/LocalDictation/. User-facing `README.md`.
- **Architecture decisions: `docs/adr/`** (17 ADRs) — read these to understand *why* things are the way they are.
- **macOS port design + distribution-readiness specs:** `docs/superpowers/specs/2026-07-07-macos-port-design.md`
  (the original port), `docs/superpowers/specs/2026-07-13-mac-distribution-readiness.md` (gaps found
  and closed via real on-device testing — read this before touching mac-specific code).

---

## Solution layout (Clean Architecture)

| Project | Role |
|---|---|
| `LocalDictation.Domain` | Entities/enums: `AudioClip`, `Transcript`, `HistoryEntry`, `SpeechModelSize`, `ProcessingMode`. No dependencies. |
| `LocalDictation.Application` | Abstractions (`ISpeechEngine`, `IOllamaLifecycle`, `IOllamaInstaller`, `IAudioCaptureService`, `IOverlayController`, `ITextProcessor`, `ISpeechModelManager`, …), `AppSettings`, the `DictationPipeline`. |
| `LocalDictation.Infrastructure` | **Portable core, shared by both platforms** (plain `net8.0`, no OS-specific TFM): `WhisperNetEngine`, `OllamaLifecycle` + `OllamaTextProcessor`, `SpeechModelManager`, SQLite history, `AppPaths` (branches on `OperatingSystem.IsMacOS()` for `~/Library/Application Support` vs `%LocalAppData%`). |
| `LocalDictation.Infrastructure.Windows` | `net8.0-windows` + `UseWPF`: `NAudioCaptureService`, `HotkeyService` (Win32 `RegisterHotKey`), `Win32Inspector` (UIA), output targets (Clipboard/SendInput/UIA), `TtsDictationSelfTest`, `WindowsOllamaInstaller`. Registered via `AddWindowsInfrastructure()`. |
| `LocalDictation.Infrastructure.Mac` | Plain `net8.0` (compiles anywhere, only *runs* on macOS — every type is `[SupportedOSPlatform("macos")]`): `CoreAudioCaptureService` (AudioQueue), `CarbonHotkeyService`, `AxWindowInspector` (Accessibility API + a `CGWindowListCopyWindowInfo` fallback — see gotchas), output targets (Pasteboard/CGEvent/AX-value), `SaySelfTest`, `MacOllamaInstaller`. Registered via `AddMacInfrastructure()`. |
| `LocalDictation.Desktop` | WPF app: `Program.cs` (Velopack + WPF entry point), `App.xaml.cs` (DI boot), `DictationController`, views (`OverlayWindow`, `ControlPanelWindow`, `HistoryWindow`, `OnboardingWindow`), view models, `TrayHost`, `UpdateService`, `Themes/Theme.xaml` (incl. the `AppMark` DrawingImage). |
| `LocalDictation.Desktop.Avalonia` | macOS app: `Program.cs` (Avalonia entry point), `App.axaml.cs` (DI boot, mirrors WPF's — see parity note below), `MacDictationController`, Avalonia views (same names as WPF's), `MenuBarNotificationService` (osascript-based, since Avalonia has no native toast API), `LaunchAgentRegistration` (autostart). No Velopack/auto-update. |
| `LocalDictation.Evals` | Offline WER/latency/LLM evaluation harness with fixtures. |
| `*.Tests` | Unit + NetArchTest architecture tests (17 total) — reference only the portable core, so they run on either platform. |

**Key flow (both platforms):** hotkey → `*DictationController` inspects the focused target (privacy
blocks) → shows the capsule overlay → the platform's `IAudioCaptureService` captures 16 kHz mono →
on stop, `DictationPipeline` runs `WhisperNetEngine` (+ optional `OllamaTextProcessor`) →
`*OutputRouter` inserts via clipboard/keystroke/UIA-or-AX-value → the final text is also left on
the clipboard for re-pasting.

**`App.xaml.cs` (WPF) vs `App.axaml.cs` (Avalonia) parity:** kept in sync deliberately, but two
differences are correct, not bugs — onboarding is modal (`ShowDialog()`) on Windows vs non-modal
(`.Show()` + resume-on-`Closed`) on Mac (Avalonia has no equivalent owner-window blocking model for
a tray-only app), and Windows has no `UpdateService`/Velopack equivalent on Mac. Both now wire
`AppDomain.CurrentDomain.UnhandledException` → `StartupLog` (added to the Mac side 2026-07-19 — it
was silently missing, so crashes outside `Boot()`'s own try/catch left zero trace).

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
dotnet test  LocalDictation.sln --nologo            # 43 tests
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
**`%LocalAppData%\LocalDictation\models\whisper`** on Windows (also the Velopack install root, so
app-data sits beside the versioned `current\` dir) or **`~/Library/Application Support/LocalDictation/models/whisper`**
on macOS, or via the repo probe / `LOCALDICTATION_MODELS` env var in dev.
Dev-installed: `ggml-base.en.bin`, `ggml-small.bin`. Build artifacts live under `artifacts/` (gitignored).

---

## Building/running on macOS

`dotnet build LocalDictation.sln` **fails on macOS** for the Windows-only projects
(`Infrastructure.Windows`, `Desktop`) — they're `net8.0-windows`/`UseWPF`, which needs
`-p:EnableWindowsTargeting=true` even just to compile-check (never to run). Build the mac set by
project, not the whole solution:

```bash
# .NET 8 SDK: if not installed, use the official script (not the brew cask — it needs sudo with no
# TTY for password entry in an agent session). Installs to ~/.dotnet, no admin rights needed:
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 8.0.417 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"   # add to ~/.zshrc to persist — note GUI apps (VS Code, etc.)
                                     # launched from Dock/Spotlight don't read ~/.zshrc at all, only
                                     # Terminal/interactive shells do; restart such apps after adding it.

dotnet build src/LocalDictation.Infrastructure.Mac/LocalDictation.Infrastructure.Mac.csproj -c Debug --nologo
dotnet build src/LocalDictation.Desktop.Avalonia/LocalDictation.Desktop.Avalonia.csproj -c Debug --nologo
dotnet test  tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --nologo         # 14 tests
dotnet test  tests/LocalDictation.Architecture.Tests/LocalDictation.Architecture.Tests.csproj --nologo  # 3 tests

# Quick iteration: run the bare assembly directly (shows as generic "dotnet" in Dock/App Switcher,
# not "LocalDictation" — fine for dev, but see the bundle note below for anything permission-related).
dotnet src/LocalDictation.Desktop.Avalonia/bin/Debug/net8.0/LocalDictation.dll

# Real .app bundle (needed to test codesigning/entitlements/notarization, or anything where a stable
# bundle identity matters for TCC permission grants — the bare dotnet process gets a generic identity):
dotnet publish src/LocalDictation.Desktop.Avalonia/LocalDictation.Desktop.Avalonia.csproj \
  -c Release -r osx-arm64 --self-contained -o /tmp/mac-publish
build/macos/make-app-bundle.sh /tmp/mac-publish /tmp/LocalDictation.app 1.0.7-dev arm64
codesign --sign "<identity>" --entitlements build/macos/entitlements.plist --options runtime /tmp/LocalDictation.app
open /tmp/LocalDictation.app
```

**macOS-specific gotchas found via real on-device testing (2026-07-19 — see the distribution-readiness
spec for the full writeup):**
- **`build/macos/` is caught by the repo's `.gitignore` `[Bb]uild/` rule** (meant for build *output*
  dirs, but also matches this legitimate *source* directory). Existing tracked files there are
  unaffected, but new ones need `git add -f`.
- **Codesigning needs `--entitlements build/macos/entitlements.plist`.** Signing with
  `--options runtime` (Hardened Runtime) and no entitlements crashes the app instantly on launch —
  `Failed to create CoreCLR, HRESULT: 0x80070008` — because Hardened Runtime blocks JIT and
  cross-signed-library loading by default, both of which CoreCLR needs. Reproduced and fixed
  2026-07-19; `release-macos.yml`'s codesign step now passes it.
- **`CoreAudioCaptureService.Stop()` must not hold its lock across `AudioQueueStop(queue, true)`** —
  that native call blocks until any in-flight buffer callback returns, and the callback needs the
  same lock, so holding it across the call deadlocks (resolved only by CoreAudio's own teardown
  timeout, observed as a reproducible ~29s stall on *every* dictation before the fix).
- **The systemwide `AXFocusedApplication`/`AXFocusedUIElement` AX attributes return
  `kAXErrorNoValue` in practice**, even with Accessibility permission granted — a real AXError, not
  a permission failure. `AxWindowInspector` falls back to `CGWindowListCopyWindowInfo` (CoreGraphics,
  no Objective-C needed) for the frontmost pid, and to an app-scoped
  `AXUIElementCreateApplication(pid)` query for the focused element — but **that also returns
  `kAXErrorNoValue` for Chromium/Electron targets** (confirmed against VS Code): their accessibility
  tree is built lazily and needs an `AXEnhancedUserInterface` activation handshake this class doesn't
  attempt. Net effect: `TargetControl.IsEditable`/`IsSensitive` stay unset for VS Code/Chrome/Slack/
  Discord/Notion-style apps. Dictation itself is unaffected; the sensitive-field privacy block and
  the "editor fallback" heuristic don't trigger for those apps. **Don't gate output-routing
  `CanHandle()` on `IsEditable`** to "fix" this — it would open the floating editor on every
  dictation into the most common targets, a regression, not a fix.
- **`osascript` invocations must use `ProcessStartInfo.ArgumentList`, never a joined `Arguments`
  string** — the AppleScript source itself contains double quotes, so .NET's re-splitting of a
  pre-joined string collides with them, leaking words from the dictated text out as bare (and
  invalid) AppleScript tokens.
- **`MenuBarNotificationService`'s banner requires Focus/Do Not Disturb to be off** — like any
  macOS notification, Focus mode silently suppresses it with no error; this is expected OS behavior,
  not a bug to chase.

---

## Current feature state (as of 2026-07-06 — shipped as v1.0.6)

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

*Reliability & diagnostics (shipped v1.0.5 — ADR-0015/0016):* dictation
failures now show the **real reason** (`DictationFailure` + `FailureMessages`) instead of a blanket
"No speech detected"; `ISpeechEngine.Status` (`SpeechEngineStatus`/`SpeechReadiness`) with explicit
**native-library-missing** detection + `ReloadAsync`; **`IReadinessService`** pre-flight gate in
`DictationController.StartAsync` (blocks doomed recordings when speech/mic is down); **Settings ›
System status** section (live health rows, **Reload model**, mic-free **Test dictation** self-test via
`IDictationSelfTest`/`TtsDictationSelfTest` using Windows TTS — `System.Speech` now referenced by
Infrastructure); **glass floating editor** redesigned (translucent + full min/max/close chrome) and
now also opens when the **foreground window changed** since capture (`OutputRouter` guard +
`AppSettings.EditorOnFocusLoss`, on by default). Self-test verified PASS end-to-end against `base.en`.

*Capsule enhancements (shipped v1.0.6 — ADR-0017):* the listening pill now shows a **live mic-mute
indicator** (`IAudioCaptureService.IsInputMuted()`; red mic-slash when muted; `OverlayController` polls
it while shown) and the **real focused-app icon** (`TargetControl.ExecutablePath` → `AppIconProvider`
via `SHGetFileInfo`, cached, falls back to `AppMark`).

*Context-aware personas (shipped — ADR-0018):* AI enhancement prompts are now data
(`personas.json`), auto-resolved from the focused app's process name (Notion/Email/Teams built-in)
with a **primary-hotkey auto path** (only when AI is on) and a **second hotkey (`Ctrl+Alt+Space`)
persona picker** that force-enables AI for one dictation regardless of the global toggle. Personas
never gate whether AI runs, only which prompt it uses; the four legacy `ProcessingMode` prompts are
now editable "System" personas with Reset-to-default. Import merges (adds/updates User personas,
never overwrites System/BuiltIn seeds) so sharing `personas.json` is non-destructive. AI enhancement
also now uses a larger Ollama context window + longer timeout and always falls back to the raw
transcript on failure — a safety subset, not the full long-dictation feature: **a chunk/merge engine
for genuinely long (20-30 min) dictations is deferred**, own spec.

**Dropped:** live text preview (tiny-model rolling preview) — reverted (ADR-0009). Don't re-add unless asked.

**Previously-known gaps now FIXED (don't re-flag):** boot-time Ollama start when `AiEnabled`; the
startup Run key (Velopack gives a stable install path, so it no longer points at a dev build).

---

## macOS port (shipped v1.0.7, hardened 2026-07-19)

The port itself (Avalonia UI shell, `Infrastructure.Mac` adapters, `release-macos.yml` `.dmg`
pipeline, dual-platform docs) landed across PRs #5–#8 and shipped in **v1.0.7** — both
`LocalDictation-osx-Setup.dmg` (arm64) and `LocalDictation-osx-x64-Setup.dmg` (x64) are confirmed
attached to that release. It sat entirely compile-checked-but-never-run until 2026-07-19, when it
was first actually built and exercised end-to-end on real Mac hardware, surfacing and fixing:
`AppPaths`/`StartupLog` resolving to the wrong (Linux-style) data directory; a `CoreAudioCaptureService`
lock/native-call deadlock causing a ~29s stall on every dictation; a missing codesign entitlements
plist that would have crashed every real signed release on launch; unreliable systemwide AX
attributes causing the target app to always show "unknown" (fixed via a `CGWindowListCopyWindowInfo`
fallback); an `osascript` argument-quoting bug breaking every notification banner; and a missing
`AppDomain.UnhandledException` handler on the Mac boot path. `OllamaLifecycle`'s install-detection
was also extracted behind a new `IOllamaInstaller` port (`WindowsOllamaInstaller`/`MacOllamaInstaller`)
since it had been hardcoded to Windows-only paths despite living in the "portable" core project. See
`docs/superpowers/specs/2026-07-13-mac-distribution-readiness.md` for the full investigation and the
`fix/macos-runtime-bugs` branch for the fixes. **Latest published release: v1.0.7.**

**Deferred / genuinely open:**
- **Windows code-signing** — unsigned installs show a SmartScreen "More info → Run anyway" prompt (fix:
  Azure Trusted Signing ~$10/mo or an OV cert).
- **macOS code-signing/notarization** — same idea, different mechanism: needs an Apple Developer
  Program enrollment ($99/yr), a Developer ID Application cert, and 6 GitHub secrets
  (`MACOS_CERT_P12`, `MACOS_CERT_PASSWORD`, `MACOS_SIGN_IDENTITY`, `MACOS_NOTARY_APPLE_ID`,
  `MACOS_NOTARY_PASSWORD`, `MACOS_NOTARY_TEAM_ID`) — the workflow's codesign/notarize/staple steps
  already exist and auto-activate once those secrets are present; no code change needed.
- **No auto-update on macOS** — Velopack doesn't support it; today "update" means re-downloading the
  `.dmg`. A conscious deferral, not an oversight (see the distribution-readiness spec).
- **The "no input selected" floating-editor fallback rarely triggers** — the primary clipboard
  output strategy reports success even when nothing is actually focused/editable, so
  `EditorReason.InsertFailed` almost never fires. Not fixed: doing so safely needs the AX
  editable-detection reliability problem above solved first (see that gotcha) — the dictated text is
  never lost either way, since it's always copied to the clipboard regardless of where/whether it
  was inserted.
- Minor: the history **search drawer overlaps** slightly on open (user OK'd it); the ~163 MB
  uncompressed exe bundles whisper.net backends for all RIDs and could be trimmed.
