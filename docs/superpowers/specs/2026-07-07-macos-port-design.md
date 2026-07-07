# LocalDictation — macOS Port Design

**Date:** 2026-07-07
**Status:** Implemented — all 6 phases landed. Windows build/tests/E2E green (no regression);
`Infrastructure.Mac` + `Desktop.Avalonia` compile + self-contained-publish clean for `osx-arm64`;
`.dmg` pipeline + dual-platform docs in place. macOS runtime E2E remains mac-only (CI artifact / a Mac).
**Author:** Kunal Kumar

## Goal

Ship a macOS build of LocalDictation that replicates every Windows feature — the listening
capsule, control-panel settings, menu-bar (tray) item, first-run onboarding, transcript history,
floating fallback editor, Ollama AI enhancement, autostart, notifications, and auto-update —
reusing the existing Clean-Architecture .NET core. Update README + the docs site to cover both
platforms. Add a GitHub Actions pipeline that produces an installable `.dmg` on every release.
Validate the Windows build end-to-end so the restructure introduces no regression.

### Decisions (locked)

- **UI/platform stack: Avalonia UI** (`net8.0`, cross-platform XAML/MVVM). Maximizes reuse of the
  existing `Domain`/`Application`/`Shared`/`Plugins.Abstractions` core; only the Windows-only
  Infrastructure adapters and the UI shell are new.
- **macOS signing: unsigned now, sign-ready later.** The pipeline emits an unsigned `.dmg` today
  (no Apple Developer account required); codesign/notarize/staple steps are present but auto-skip
  when the Apple secrets are absent, and activate once they exist. Mirrors the current
  unsigned-Windows / SmartScreen posture.

## Hard constraint (stated explicitly)

The development machine is Windows. A macOS `.app`/`.dmg` cannot be **built into a bundle, codesigned,
notarized, or run** on Windows — Apple's toolchain and the macOS whisper/audio natives are macOS-only.
Therefore:

- The `.dmg` is produced by a GitHub Actions **`macos-latest`** runner (authored here, runs in CI).
- macOS **runtime E2E** (launching the app, granting Accessibility/Mic permission, dictating) can
  only happen on a Mac — the owner's Mac or by downloading the CI artifact. It is out of scope for
  local verification from Windows.
- What *is* locally verifiable from Windows: (a) the full **Windows regression E2E** after the
  Infrastructure split, and (b) **compile-clean** builds of the core + `Infrastructure.Mac` +
  `Desktop.Avalonia` for `osx-arm64` (P/Invoke declarations compile without macOS; only execution
  needs it).

## Architecture

### Solution restructure

`Infrastructure` is currently `net8.0-windows` with `UseWPF=true` and unconditionally registers
Win32 adapters — this blocks any macOS build. Split into three projects; the pure-`net8.0` core is
shared, each platform gets its own adapter assembly.

| Project | TFM | Role |
|---|---|---|
| `LocalDictation.Infrastructure` (core) | `net8.0` | Portable adapters: `WhisperNetEngine`, `SpeechModelManager`, `OllamaTextProcessor`, `OllamaLifecycle` (core probe/start/pull), `NoOpTextProcessor`, `SqliteHistoryRepository`, `JsonSettingsStore`, `AppPaths`, `ReadinessService`, `PluginHost`. Exposes `AddCoreInfrastructure()`. |
| `LocalDictation.Infrastructure.Windows` (new; existing code moved) | `net8.0-windows` (`UseWPF` only if UIA still needs it) | `NAudioCaptureService`, `HotkeyService`, `Win32Inspector`, `ClipboardOutputTarget`/`SendInputOutputTarget`/`UiaOutputTarget`, `OutputRouter` (Win foreground check), `TtsDictationSelfTest`, `AppIconProvider`, `StartupRegistration`, Windows Ollama-install path. Exposes `AddWindowsInfrastructure()`. |
| `LocalDictation.Infrastructure.Mac` (new) | `net8.0`, published with `RuntimeIdentifier=osx-arm64`/`osx-x64` | macOS adapters (below). Exposes `AddMacInfrastructure()`. |

Unchanged: `Domain`, `Shared`, `Application`, `Plugins.Abstractions` (all pure `net8.0`).

`OutputRouter`'s orchestration is portable; only its foreground-window probe is platform-specific —
extract that probe behind a tiny seam (or keep one `OutputRouter` per platform project). The core
`OllamaLifecycle` keeps the portable probe/`ollama serve`/`ollama pull` logic; the OS-specific
install flow (Windows `OllamaSetup.exe` vs mac install) lives behind an `IOllamaInstaller` port
implemented per platform.

### macOS adapters (`Infrastructure.Mac`) — port → framework

All native access is P/Invoke into system frameworks; the only new native NuGet is the whisper
macOS runtime. No third-party native audio lib.

| Application port | macOS adapter | System framework |
|---|---|---|
| `IAudioCaptureService` | `AvAudioCaptureService` | AVFoundation `AVAudioEngine` (16 kHz mono tap) + Accelerate `vDSP` FFT → 13 bands; RMS VAD; device list via CoreAudio; mute query |
| `IHotkeyService` | `CarbonHotkeyService` | Carbon `RegisterEventHotKey` / `InstallEventHandler` (parse the same `"Ctrl+Shift+Space"` string → key + modifier masks) |
| `IWindowInspector` | `AxWindowInspector` | ApplicationServices `AXUIElement` (`kAXFocusedUIElement`, role, `kAXValue`, secure-text detection) + AppKit `NSWorkspace.frontmostApplication`; needs Accessibility permission |
| `IOutputTarget` (clipboard) | `PasteboardOutputTarget` | AppKit `NSPasteboard` + CoreGraphics `CGEvent` Cmd+V |
| `IOutputTarget` (keystroke) | `CgEventKeystrokeTarget` | CoreGraphics `CGEventKeyboardSetUnicodeString` |
| `IOutputTarget` (AX value) | `AxValueOutputTarget` | `AXUIElementSetAttributeValue(kAXValueAttribute)` |
| `IDictationSelfTest` | `AvSpeechSelfTest` | `AVSpeechSynthesizer` → buffer → 16 kHz mono into the engine |
| App icon (capsule) | `NsWorkspaceIconProvider` | `NSWorkspace.icon(forFile:)` for the frontmost app's bundle path |
| Autostart | `LaunchAgentRegistration` | write/remove `~/Library/LaunchAgents/com.localdictation.app.plist` |
| Ollama install | `MacOllamaInstaller` | detect `ollama` on PATH / `/usr/local/bin` / `/opt/homebrew/bin`; guide to `Ollama.app` download if absent |

Whisper: swap `Whisper.net.Runtime` for the macOS runtime package so the `.app` ships the correct
`libwhisper`/`ggml` dylibs beside the managed assembly (published multi-file, not single-file — same
native-probing reason as Windows).

`AppPaths` on macOS should resolve `~/Library/Application Support/LocalDictation` (models, settings,
history, `startup.log`) rather than the Linux-style `~/.local/share` that `SpecialFolder` would give —
add an explicit macOS branch.

### macOS UI shell — `LocalDictation.Desktop.Avalonia` (`net8.0`, RID `osx-arm64`/`osx-x64`)

Reuses the existing MVVM view models where platform-neutral (`ControlPanelViewModel`,
`HistoryViewModel`, `OnboardingViewModel`, `StatusItemViewModel` — CommunityToolkit.Mvvm runs under
Avalonia). New Avalonia views + an Avalonia composition root:

- **Listening capsule** — borderless transparent Avalonia `Window`, bottom-center,
  non-activating; pulsing dot, 13-bar frequency-reactive waveform (same spectrum data contract via
  `IOverlayController`), frontmost-app icon + name, mic-mute glyph (red mic-slash), gold processing
  shimmer. Avalonia `OverlayController` + `AvaloniaUiDispatcher`.
- **Control panel** — Avalonia restyle of the Win11 SettingsCard pattern (rounded card per setting,
  section headers, immediate-apply). Same sections: **System status** (health rows, Reload model,
  Run self-test, Refresh), **Dictation** (mic picker, speech-model picker, hotkey), **AI enhancement**
  (toggle → `IOllamaLifecycle`, cleanup mode, model), **History** (retention days / keep-forever,
  Open History), **General** (start-at-login, notify-on-complete, editor-on-focus-loss).
- **History window** — search box, favorites-only toggle, per-row copy/star/delete.
- **Onboarding** — 5-step wizard (Welcome → Mic check → Model download → optional AI → Ready),
  shown on first run when no model is installed.
- **Floating editor** — dark-glass fallback (`IFloatingEditor`) with text + reason + Copy.
- **Menu-bar item** — Avalonia `TrayIcon` → NSStatusItem with the same menu (Dictate now / History /
  Control panel / Quit). Notifications via `INotificationService` (mac adapter over
  `UNUserNotification`, falling back to a lightweight banner if unavailable).
- **Theme** — monochrome black/white + single gold processing accent, ported to an Avalonia `Styles`
  resource dictionary (`Switch`, `Win11Card`→`SettingsCard`, `GroupHeader`, combo/text/ghost button,
  thin scrollbar). App mark reused as an Avalonia `DrawingImage`; generate `AppIcon.icns` for the
  bundle.
- **Entry point** — Avalonia `AppBuilder`; DI boot mirrors `App.xaml.cs` (load settings, build
  container with `AddCoreInfrastructure()+AddMacInfrastructure()` + Avalonia UI ports, init history +
  prune, onboarding if no model, wire menu-bar events, autostart, warm Ollama if `AiEnabled`).
  Auto-update via Velopack's macOS support if viable; otherwise the `.dmg` is the update channel and
  the workflow publishes each version to Releases.

`DictationController` and `DictationPipeline` are reused unchanged (they depend only on ports).

## Packaging pipeline

New workflow `.github/workflows/release-macos.yml`, `runs-on: macos-latest`, triggered by the same
`v*` tags + manual dispatch as `release.yml`:

1. Setup .NET 8 (pinned via `global.json`).
2. `dotnet publish src/LocalDictation.Desktop.Avalonia -c Release -r osx-arm64 --self-contained` (and
   an `osx-x64` pass; optionally lipo into a universal build — arm64 first, x64 if low-cost).
3. Assemble `LocalDictation.app`: `Contents/MacOS` (published output), `Info.plist`
   (`NSMicrophoneUsageDescription`, Accessibility rationale, bundle id `com.localdictation.app`,
   `LSUIElement=true` so it's a menu-bar-only agent), `Resources/AppIcon.icns`, whisper dylibs beside
   the exe.
4. **Codesign + notarize + staple** — steps present but guarded by `if: secrets present`. Uses
   `codesign --deep --options runtime`, `xcrun notarytool submit --wait`, `xcrun stapler staple`.
   Skipped cleanly when Apple secrets are absent (unsigned build still produced).
5. `create-dmg` → `LocalDictation-osx-Setup.dmg` (or `-arm64`/`-x64`), attach to the GitHub Release.

`ci.yml` gains a parallel `build-mac` job on `macos-latest`: restore + build the core + `Application`
+ `Infrastructure.Mac` + `Desktop.Avalonia` for `osx-arm64` and run the cross-platform unit tests.
The existing `windows-latest` job and `release.yml` are unchanged.

The `.sln` gets the three new projects but is arranged so `windows-latest` builds the Windows set and
`macos-latest` builds the Mac set (solution filters or per-job `dotnet build <project>` targeting to
avoid cross-OS project failures).

## Documentation

- **README.md** — retitle "System-wide, offline, AI voice dictation for **Windows and macOS**".
  Split Install + First-run into Windows and macOS sections; macOS gets `.dmg` download, drag-to-
  Applications, first-launch right-click→Open (Gatekeeper), and the Accessibility + Microphone
  permission grants. "How it's made" notes Avalonia (macOS) alongside WPF (Windows) over the shared
  core.
- **docs/index.html** — add a "Download for macOS" button in the hero; add a macOS adapter column to
  the ports→adapters table (AVAudioEngine, Carbon hotkey, AXUIElement, CGEvent/NSPasteboard);
  mention Avalonia + the `.app`/`.dmg` distribution tier.

## Testing & validation

- **Windows regression E2E (local, mandatory):** after the Infrastructure split — clean-rebuild the
  Desktop project (`Remove-Item obj,bin`), launch the freshly built `LocalDictation.exe`, exercise
  dictation against a fixture WAV (play `Evals/.../fixtures/f1.wav` while recording), confirm text is
  produced and inserted, capsule/menu/settings/history all function. Run all 17 unit + architecture
  tests green.
- **Mac compile validation (local):** `dotnet build` core + `Infrastructure.Mac` + `Desktop.Avalonia`
  for `-r osx-arm64` — must compile clean on Windows.
- **Mac runtime validation (CI/owner):** the `macos-latest` CI job builds and the release job produces
  a downloadable `.dmg`; actual launch/permission/dictation E2E is performed on a Mac. Explicitly
  called out as not locally verifiable from Windows.

## Phasing (focused commit series)

1. **Infra split** → core + `Infrastructure.Windows`; rewire DI (`AddCoreInfrastructure` +
   `AddWindowsInfrastructure`) and test project references. **Gate: Windows builds, 17 tests pass, app
   runs unchanged.**
2. **`Infrastructure.Mac`** adapters — compile-clean for `osx-arm64`.
3. **`Desktop.Avalonia`** shell — windows, menu-bar, theme, DI boot; compile-clean for `osx-arm64`.
4. **Pipelines** — `release-macos.yml` + `ci.yml` mac job; solution filtering.
5. **Docs** — README + `docs/index.html` dual-platform.
6. **Validation** — Windows E2E regression pass + writeup; confirm mac CI job green.

## Out of scope / deferred

- Apple codesigning/notarization certificates (workflow is ready; secrets added later).
- macOS auto-update polish if Velopack-osx proves unviable (fallback: `.dmg` re-download).
- Universal binary is optional; arm64 is the primary target, x64 added if low-cost.
- No new features — this is a faithful replica of the current v1.0.6 Windows surface, nothing more.
