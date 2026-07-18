# Making LocalDictation genuinely mac-distributable

## Context

The ask was a full plan to port LocalDictation to macOS and make it distributable. Investigation
turned up something not obvious from `CLAUDE.md` alone: **the macOS port already exists and has
shipped.** `CLAUDE.md` (which says "read fully every session") never mentions it — it's stale — but
the git history tells the real story:

- A design spec (`docs/superpowers/specs/2026-07-07-macos-port-design.md`) was written, and all 6
  phases landed via 6 real commits: Infrastructure split (core/Windows), `Infrastructure.Mac` adapters,
  `Desktop.Avalonia` UI shell, a `.dmg` CI pipeline, dylib-staging fix, dual-platform docs.
- It's merged to `main` (PRs #5–#8) and released: **v1.0.7** on GitHub Releases lists "macOS port:
  Avalonia UI + Infrastructure.Mac + .dmg pipeline" with 10 attached assets.
- `.github/workflows/release-macos.yml`'s most recent run **succeeded** (1m56s, `macos-latest`) after
  two earlier failed attempts were fixed.
- No stub/`NotImplementedException`/TODO markers in `Infrastructure.Mac` or `Desktop.Avalonia` — this
  isn't scaffolding, it's a complete implementation (AVAudioEngine/AudioToolbox capture, Carbon hotkey,
  `AXUIElement` focused-control + secure-text detection, `CGEvent`/`NSPasteboard` output, `NSWorkspace`
  icon lookup, `LaunchAgent` autostart, Ollama-on-mac detection, Whisper.net's real cross-platform
  runtime package staged into the `.app` bundle).

So the plan is **not** "design a port" — it's "close the specific, named gaps between what's built and
true frictionless distribution."

## What's genuinely still missing

1. **Code signing + notarization — the #1 blocker for painless distribution.** The workflow already has
   full codesign/notarize/staple steps (`release-macos.yml:86-160`), but they're gated behind GitHub
   secrets that don't exist yet (`env.MACOS_CERT_P12 != ''`) — so every `.dmg` today ships **unsigned**.
   On a fresh Mac, Gatekeeper will refuse to launch it outright (not just warn) unless the user
   right-clicks → Open, which is real friction for anyone downloading it as a normal app.
2. **Zero real-hardware validation — but this is now fixable directly.** The design doc's "hard
   constraint" (dev machine is Windows, mac code only ever compile-checked) no longer applies: this
   session runs on the owner's own MacBook Air (arm64, Darwin 25.5.0), with Xcode fully installed and a
   valid "Apple Development" codesigning identity already in the keychain. `dotnet` is **not currently
   installed** on this Mac — that's the one missing prerequisite — but once it is, the mac-only code
   (Carbon hotkey, `AXUIElement`, `AVAudioEngine`, Accessibility/Microphone TCC prompts) can actually be
   built and run for real, not just compiled. The existing signing identity is **Apple Development (free
   tier)** — good enough for local run/debug signing, but not the paid **Developer ID Application** cert
   needed for notarized public distribution (that's still Phase 3).
3. **No auto-update on mac** — deliberately deferred (Velopack doesn't support macOS). Today "update"
   means manually redownloading the `.dmg`. Worth a conscious decision, not an oversight to silently fix.
4. **Unverified asset completeness** — the release page's asset list failed to load on check, so it's
   not directly confirmed that both `LocalDictation-osx-Setup.dmg` (arm64) and
   `LocalDictation-osx-x64-Setup.dmg` are actually attached to v1.0.7, even though the workflow builds both.
5. **`CLAUDE.md` is stale** — it describes this as a Windows-only project and omits the entire mac port,
   the Avalonia stack, and the new project layout. Anyone (human or Claude) starting a session from that
   file alone gets a wrong picture of the codebase.
6. **`AppPaths.cs` has no macOS branch** (`src/LocalDictation.Infrastructure/AppPaths.cs:32-33`) —
   unconditionally resolves `Environment.SpecialFolder.LocalApplicationData`, which on macOS maps to
   `~/.local/share` (Linux-style), not `~/Library/Application Support`. Both `Desktop.Avalonia/App.axaml.cs:62`
   and the WPF `App.xaml.cs:55` construct `new AppPaths()` with no override, so nothing upstream
   compensates. Models/settings/history land in the wrong place on a real Mac.
7. **`OllamaLifecycle.cs` (`src/LocalDictation.Infrastructure/Ai/OllamaLifecycle.cs`) is not actually
   OS-agnostic** despite living in the portable core project — it hardcodes
   `%LocalAppData%\Programs\Ollama\ollama.exe` detection and shells out to `OllamaSetup.exe` for install.
   On macOS this silently no-ops (AI enhancement just never auto-installs Ollama) instead of crashing —
   quieter but still broken. The design doc's `IOllamaInstaller` seam was never actually built; no
   `MacOllamaInstaller` exists anywhere in `Infrastructure.Mac`.

## Plan

### Phase 1 — Real build + run on this Mac, plus fixes for the confirmed bugs
This session runs on the owner's actual Mac, so this phase is a full end-to-end local validation pass,
not just a read-only review:
1. **Install .NET 8 SDK** (`global.json` pins `8.0.417`, `rollForward: latestFeature`) via
   `brew install --cask dotnet-sdk` or the official `dotnet-install.sh` script pinned to that version —
   whichever lands a matching 8.0.4xx SDK. Confirm with `dotnet --list-sdks`.
2. **Fix the two confirmed bugs** (`AppPaths.cs`, `OllamaLifecycle.cs`/`MacOllamaInstaller`) — see the
   detailed implementation section below.
3. **Build for real**: `dotnet build LocalDictation.sln` (or targeted projects) and
   `dotnet publish src/LocalDictation.Desktop.Avalonia -c Release -r osx-arm64 --self-contained` —
   confirm it actually compiles AND that `libwhisper.dylib`/`libggml*.dylib` land in the publish output
   (this was a real bug once, fixed in commit `0164d73` — worth re-confirming for real instead of trusting
   the CI log).
4. **Run it.** Launch the published `.app`/binary directly, grant Microphone + Accessibility permissions
   when macOS prompts (this requires the owner's interactive click through the TCC dialogs), then
   exercise: global hotkey (Carbon), dictation into a real text field (`AXUIElement`/`CGEvent` output
   paths), the menu-bar item, control panel, history window. This directly resolves the design doc's
   "never actually run" gap instead of deferring it.
5. Optionally `brew install ollama` to test the AI-enhancement path end-to-end too (not currently
   installed on this Mac).
6. Read the remaining `Infrastructure.Mac` files for correctness issues that only show up at runtime
   (`Input/CarbonHotkeyService.cs`, `Interop/AxWindowInspector.cs` + `Accessibility.cs`,
   `Audio/CoreAudioCaptureService.cs` + `SpectrumAnalyzer.cs`, `Output/MacOutputRouter.cs` +
   `MacOutputTargets.cs`, `Diagnostics/SaySelfTest.cs`) and fix anything the live run surfaces.
7. Diff `Desktop.Avalonia/App.axaml.cs` against WPF's `App.xaml.cs` for behavioral parity (onboarding
   gate, autostart registration, Ollama warm-up on `AiEnabled`).

### Phase 2 — Verify the release is actually complete
- Re-check the v1.0.7 release assets (`gh release view v1.0.7` if `gh` is installed, else the web UI) to
  confirm both arm64 and x64 `.dmg`s are attached, plus checksums if the workflow produces them.
- Skim `release-macos.yml` end to end once more for the `Info.plist` step — confirm
  `NSMicrophoneUsageDescription` and the Accessibility usage string are present and read sensibly to an
  end user seeing the permission prompt (this is a common App Review / user-trust detail, not just a
  technicality).
- Since local codesigning is now testable: `codesign --sign "Apple Development: keertis0299@gmail.com
  (4Q7RQ65W33)" LocalDictation.app` (ad-hoc/local identity, not the notarization-ready Developer ID) to
  confirm the bundle structure itself is codesign-valid before the paid cert is in play.

### Phase 3 — Apple signing enrollment (owner action, not executable by an agent)
Requires an Apple Developer Program account:
1. Enroll in the Apple Developer Program ($99/yr) if not already.
2. Create a **Developer ID Application** certificate in Xcode or the developer portal, export as `.p12`.
3. Generate an app-specific password for notarization (appleid.apple.com → Sign-In and Security).
4. Add these 6 repo secrets (names already wired into `release-macos.yml`, no workflow changes needed):
   `MACOS_CERT_P12`, `MACOS_CERT_PASSWORD`, `MACOS_SIGN_IDENTITY`, `MACOS_NOTARY_APPLE_ID`,
   `MACOS_NOTARY_PASSWORD`, `MACOS_NOTARY_TEAM_ID`.
5. Re-run `release-macos.yml` (or cut a new tag) — the guarded steps activate automatically; no code
   change needed.

### Phase 4 — Decide the update story
Tradeoff, not a default to silently pick: (a) leave it as manual `.dmg` redownload — simple, matches
plenty of indie mac apps, zero extra code; (b) build a lightweight in-app "check GitHub latest release,
prompt to redownload" notice — small, contained addition to `Desktop.Avalonia`; (c) revisit Velopack's
mac support in case it's matured since the spec was written. Recommendation: (a) now, (b) later if users
complain.

### Phase 5 — Fix `CLAUDE.md`
Update it to reflect reality: mention the mac port, `Infrastructure.Mac`/`Desktop.Avalonia`, the
`release-macos.yml` pipeline and its signing-secret gate, the `AppPaths`/`OllamaLifecycle` fixes, and the
still-open real-hardware-validation gap — so the next session starts from an accurate picture instead of
rediscovering all of this from git history again.

## Verification

- **Phase 1 (this Mac, this session):** after installing the .NET SDK and applying the fixes,
  `dotnet build`/`dotnet test` must stay green (17 tests), then publish and launch the app directly,
  grant Microphone + Accessibility permissions when macOS prompts (the owner clicks through the TCC
  dialogs), dictate into a real text field, confirm the capsule/menu-bar item/control panel/history
  window all behave correctly, and confirm app data lands under
  `~/Library/Application Support/LocalDictation`.
- **Phase 3 (after Apple secrets are added, separately):** confirm in the Actions log that the "Import
  signing certificate" / "Codesign .app bundles" / "Notarize + staple DMGs" steps actually ran (not
  skipped), then `codesign -dv --verbose=4 LocalDictation.app` and `spctl -a -vvv LocalDictation.app`
  should both report accepted/signed, and a fresh download+launch should not show the Gatekeeper
  "unidentified developer" block.

---

## Implementation plan for Phase 1 (the only phase with actual code changes)

Everything else (Phase 2 verification, Phase 3 Apple enrollment, Phase 4 update-story decision) is
either read-only checking or an owner action with no code to write. This is the concrete, file-level
plan for the two confirmed bugs plus the review pass.

### 1. `AppPaths.cs` — add the macOS branch

File: `src/LocalDictation.Infrastructure/AppPaths.cs:32-33`. Today:
```csharp
Root = root ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalDictation");
```
`SpecialFolder.LocalApplicationData` resolves to `~/.local/share` on macOS (XDG convention), not
`~/Library/Application Support`. Change to branch on `OperatingSystem.IsMacOS()`:
```csharp
Root = root ?? Path.Combine(ResolveDataRoot(), "LocalDictation");
...
private static string ResolveDataRoot() =>
    OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
```
No caller changes needed — both `App.xaml.cs:55` and `App.axaml.cs:62` call `new AppPaths()` with no
args and pick this up automatically. Update the class's `<remarks>` doc comment (currently says
"Everything lives under `%LocalAppData%/LocalDictation`") to mention the macOS path too.

### 2. `IOllamaInstaller` port — new seam, following the existing port/adapter convention

The codebase already has a clean convention for platform-specific behavior: an interface in
`LocalDictation.Application.Abstractions`, a Windows adapter registered in
`WindowsInfrastructureModule.cs`, a Mac adapter registered in `MacInfrastructureModule.cs` (see
`IAudioCaptureService`/`IHotkeyService`/`IWindowInspector` for the pattern). `IOllamaInstaller` doesn't
exist yet — build it the same way.

**New interface** — `src/LocalDictation.Application/Abstractions/IOllamaInstaller.cs`:
```csharp
public interface IOllamaInstaller
{
    bool IsInstalled();
    Task<bool> EnsureInstalledAsync(CancellationToken ct = default);
}
```

**Refactor `OllamaLifecycle.cs`** (`src/LocalDictation.Infrastructure/Ai/OllamaLifecycle.cs`):
- Add `IOllamaInstaller installer` constructor param, store as `_installer`.
- Line 59: `if (!IsOllamaInstalled() && !await EnsureInstalledAsync(ct))` →
  `if (!_installer.IsInstalled() && !await _installer.EnsureInstalledAsync(ct))`.
- Delete the private `IsOllamaInstalled()` (lines 124-136) and `EnsureInstalledAsync()` (lines 142-169)
  methods entirely — that logic moves into `WindowsOllamaInstaller`.
- `TryStartServer`/`PullModelAsync`/`WaitUntilRunningAsync`/`LoadModelAsync` stay untouched — they're
  genuinely portable (`ollama` CLI/HTTP, no OS-specific paths).

**New `WindowsOllamaInstaller`** — `src/LocalDictation.Infrastructure.Windows/Ai/WindowsOllamaInstaller.cs`:
move the deleted logic here verbatim (per-user `%LocalAppData%\Programs\Ollama\ollama.exe` check + PATH
scan for `ollama.exe`; download+run `OllamaSetup.exe` via `HttpClient`/`Process.Start`). Needs an
`HttpClient` — inject via constructor like `OllamaLifecycle` does.

**New `MacOllamaInstaller`** — `src/LocalDictation.Infrastructure.Mac/Ai/MacOllamaInstaller.cs`:
```csharp
public bool IsInstalled() =>
    File.Exists("/opt/homebrew/bin/ollama") || File.Exists("/usr/local/bin/ollama") ||
    (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator).Any(d => File.Exists(Path.Combine(d, "ollama")));

public Task<bool> EnsureInstalledAsync(CancellationToken ct) =>
    Task.FromResult(false); // no unattended mac install; caller's Failed message already points to ollama.com
```
Unlike Windows there's no unattended silent-installer flow for mac (Ollama ships as a `.app` drag-install
or `brew install ollama`) — matches the design doc's original framing ("guide to `Ollama.app` download").
`EnableAsync`'s existing failure message ("Ollama isn't installed. Get it from ollama.com.") already
covers this; no new UI needed.

**DI registration:**
- `WindowsInfrastructureModule.cs` (`src/LocalDictation.Infrastructure.Windows/DependencyInjection/`):
  add `services.AddSingleton<IOllamaInstaller, WindowsOllamaInstaller>();` alongside the existing
  `IAudioCaptureService`/`IHotkeyService` registrations.
- `MacInfrastructureModule.cs` (`src/LocalDictation.Infrastructure.Mac/DependencyInjection/`): add
  `services.AddSingleton<IOllamaInstaller, MacOllamaInstaller>();` alongside its existing registrations.
- `InfrastructureModule.cs:51-54` — update the `OllamaLifecycle` factory lambda to also resolve
  `sp.GetRequiredService<IOllamaInstaller>()` and pass it into the constructor. (Registration order
  between `AddCoreInfrastructure()` and the per-platform module doesn't matter — factories run lazily
  after the full container is built.)

### 3. Review pass on `Infrastructure.Mac` / `Desktop.Avalonia` (read, fix only clear bugs)

Read each of these for runtime-only correctness issues (nothing here changes public shape, so no
further DI/plan impact expected unless a real bug turns up):
- `src/LocalDictation.Infrastructure.Mac/Input/CarbonHotkeyService.cs` — hotkey string parsing,
  `InstallEventHandler`/`RemoveEventHandler` lifecycle (leak-on-dispose check).
- `src/LocalDictation.Infrastructure.Mac/Interop/AxWindowInspector.cs` (+ `Accessibility.cs`) —
  focused-element/secure-text detection, `NSWorkspace.frontmostApplication` icon lookup.
- `src/LocalDictation.Infrastructure.Mac/Audio/CoreAudioCaptureService.cs` + `SpectrumAnalyzer.cs` —
  device enumeration, mute query, FFT band math vs. the `IOverlayController` contract.
- `src/LocalDictation.Infrastructure.Mac/Output/MacOutputRouter.cs` + `MacOutputTargets.cs` —
  `NSPasteboard`/`CGEvent` Cmd+V path, `AXUIElementSetAttributeValue` path, foreground-app-changed guard.
- `src/LocalDictation.Infrastructure.Mac/Diagnostics/SaySelfTest.cs` — `AVSpeechSynthesizer` self-test.
- `src/LocalDictation.Desktop.Avalonia/App.axaml.cs` vs `src/LocalDictation.Desktop/App.xaml.cs` — diff
  the boot sequence (onboarding gate, autostart registration, Ollama warm-up on `AiEnabled`) for parity.

### 4. Verify `Whisper.net.Runtime` mac support — directly, for real

Now that .NET can be installed on this Mac: `dotnet publish src/LocalDictation.Desktop.Avalonia -c
Release -r osx-arm64 --self-contained -o <scratchpad>/mac-publish-check` and inspect the output
directory directly for `libwhisper.dylib`/`libggml*.dylib` — no more inferring from CI logs or NuGet
package metadata. This is exactly what commit `0164d73` ("stage whisper native dylibs into the .app")
had to fix once; confirming it's still correct is a two-minute check now.

### 5. Build + test gate

- `dotnet build` core + `Infrastructure.Mac` + `Desktop.Avalonia` for `-r osx-arm64` on this Mac — must
  be compile-clean AND is now runnable, not just checkable.
- `dotnet test LocalDictation.sln` — all 17 existing tests must still pass (the `AppPaths`/`OllamaLifecycle`
  changes touch shared core code; these are plain unit/architecture tests, so they should run fine on
  mac too — worth confirming since this is the first time they'll run here).
- Launch the built app and manually exercise dictation, hotkey, and settings as described in Phase 1
  above — the actual runtime validation this project has been missing.

### 6. `CLAUDE.md` update (Phase 5, bundled here since it's also a doc-only edit)

Add a section describing: the `Infrastructure`/`Infrastructure.Windows`/`Infrastructure.Mac` split, the
`Desktop.Avalonia` mac UI shell, the `release-macos.yml` pipeline and its signing-secret gate (list the
6 secret names), and the still-open real-hardware-validation gap. Keep it as terse as the rest of the
file's style — this repo's `CLAUDE.md` is dense reference material, not prose.
