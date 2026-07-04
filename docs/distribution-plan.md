# LocalDictation — Distribution & Packaging Plan

**Goal:** turn LocalDictation into a small, shareable, auto-updating Windows installer that fetches
the heavy assets (Whisper model, and optionally Ollama + LLM) on first run. Decision rationale:
[ADR-0014](adr/0014-velopack-distribution.md). Onboarding UX + logo: `design/onboarding-design.html`.

**Verifiable definition of done:** a clean Windows 11 machine with no .NET, no models, and no Ollama
can download one `LocalDictation-Setup.exe`, install without admin rights, complete a guided first
run that downloads a speech model, and dictate text into any app — all without touching a terminal.
AI enhancement stays off until the user opts in, at which point its ~2 GB stack downloads separately.

---

## Principles (carried from ADR-0014)

- **Tier 1 installer is small** — app + .NET runtime only. No models, no Ollama in the package.
- **Assets live in app-data, never in `current\`.** Velopack wipes/diffs `%LocalAppData%\LocalDictation\current\`
  on every update. Models, SQLite history, logs, and settings live in siblings
  (`…\models\`, `…\data\`) so updates don't destroy or re-package them.
- **AI is a separate, opt-in, later download** (ADR-0003). Skipping it must reach a fully working state.
- **No hand-rolled Run key.** Auto-start is a Velopack `Startup` shortcut through the stable install dir.

---

## Phase 0 — App-data path audit (do first; unblocks everything)

Velopack's update model makes on-disk layout a correctness issue, so fix paths before packaging.

- Audit `AppPaths` (Infrastructure) + every consumer: Whisper models (`SpeechModelManager`), SQLite
  history, `StartupLog`, `AppSettings` persistence. Confirm each resolves to
  `%LocalAppData%\LocalDictation\…` (or the `LOCALDICTATION_MODELS` override), **never the exe
  directory**.
- Confirm the existing `AppPaths` probe won't accidentally pick a path under `current\` once installed.

**Verify:** run the app from an arbitrary folder; confirm models/db/logs/settings are written under
`%LocalAppData%\LocalDictation`, not next to the exe.

## Phase 1 — Publishable, Velopack-aware build

- WPF needs an explicit entry point: set `<EnableDefaultApplicationDefinition>false</EnableDefaultApplicationDefinition>`
  in `LocalDictation.Desktop.csproj` and add a hand-written `[STAThread] static void Main(string[])`.
- Add the `Velopack` NuGet package (**flag: new dependency**). First line of `Main`, exactly once,
  before any DI/boot:
  ```csharp
  VelopackApp.Build().Run();
  ```
- Publish self-contained **multi-file** (NOT single-file):
  ```powershell
  dotnet publish src/LocalDictation.Desktop/LocalDictation.Desktop.csproj `
    -c Release -r win-x64 --self-contained true -o .\artifacts\publish
  ```
- **Gotcha (learned the hard way):** do **not** use `PublishSingleFile`. whisper.net loads its native
  `whisper.dll`/ggml backends by probing next to the exe (`runtimes/win-x64/native/`); single-file
  self-extraction places them where whisper.net can't find them, so warm-up fails with *"Native
  Library not found"* and every dictation returns *"No speech detected."* Multi-file keeps the native
  libs as loose files that load correctly. Velopack packages the whole folder either way, so the user
  still gets one `Setup.exe`.

**Verify:** launch `.\publish\LocalDictation.Desktop.exe` on a machine/VM with no .NET SDK; hotkey +
dictation work; `VelopackApp.Build().Run()` is a no-op when not installed via Velopack.

## Phase 2 — Package with `vpk`

- Install the CLI, version-matched to the NuGet package: `dotnet tool install -g vpk --version 1.2.0`.
- Produce a real app icon (`.ico`) from the chosen logo (Phase = design).
- Pack:
  ```powershell
  vpk pack --packId LocalDictation --packVersion 1.0.0 `
    --packDir .\publish --mainExe LocalDictation.Desktop.exe `
    --icon .\assets\app.ico --outputDir .\Releases
  ```

**Verify:** run `.\Releases\LocalDictation-Setup.exe` on a clean VM → installs to
`%LocalAppData%\LocalDictation\current\` with no elevation prompt; Start Menu shortcut launches it;
uninstall removes it cleanly.

## Phase 3 — Auto-update

- Add a background startup check (after warm-up, non-blocking):
  ```csharp
  var mgr = new UpdateManager(new GithubSource("https://github.com/<owner>/LocalDictation", null, false));
  var v = await mgr.CheckForUpdatesAsync();
  if (v is not null) { await mgr.DownloadUpdatesAsync(v); mgr.ApplyUpdatesAndRestart(v); }
  ```
  Guard with `mgr.IsInstalled` so dev runs skip it. (Later: surface an in-app "update ready" prompt
  instead of silent restart.)
- Publish releases: `vpk upload github --repoUrl … --token $GH_TOKEN --publish`.

**Verify:** install 1.0.0, publish 1.0.1, relaunch → app updates itself and restarts on the new version.

## Phase 4 — First-run onboarding wizard (the core UX)

Build the 6-screen flow specified in `design/onboarding-design.html`, shown when no speech model is
installed. Reuse existing services; add no cloud calls.

1. **Welcome** — promise + "downloads one small model; AI is optional/off."
2. **Mic permission + input pick** — live level meter (reuse the capsule waveform); can't proceed on a dead mic.
3. **Speech model pick + download** — curated 2–3 cards (base.en *recommended* / small / larger),
   each with size + hardware-fit line from `RecommendedForHardware()`; drives
   `ISpeechModelManager.DownloadAsync` with resumable, non-blocking, MB/total progress (gold fill).
4. **Optional AI enhancement** — single toggle **off by default**; honest "~2 GB separate download"
   copy; "Skip for now" is the prominent/happy path.
5. **Hotkey & ready** — show `Ctrl+Shift+Space`, "Start with Windows" toggle (→ Velopack `Startup`
   shortcut), live try-it box.
6. **Tray handoff** — close to tray with a one-time "lives here" callout.

**Verify:** on a profile with no model, first launch walks all screens, downloads base.en with
visible progress, and ends in a state where the hotkey dictates into Notepad — AI never touched.

## Phase 5 — Optional AI provisioning (opt-in path)

When the user enables AI (onboarding screen 4 or the control panel), and only then:

- Detect an existing Ollama (probe `/api/version`, already in `OllamaLifecycle`).
- If absent, download + silently run Ollama's official installer, then `ollama pull <model>` — each
  shown as its own separate progress card, cancelable, resumable.
- Wire the ADR-known-gap **boot-time Ollama start when `AiEnabled`** here so the AI stack is
  reboot-ready (previously deferred).

**Verify:** toggling AI on a machine without Ollama installs it + pulls the model with progress;
toggling off and rebooting with AI on brings the LLM back automatically.

## Phase 6 — Code signing (follow-up, not a blocker)

- Unsigned installs show a SmartScreen "unknown publisher" prompt ("More info → Run anyway").
  Acceptable for initial sharing.
- Fix path when ready: Azure Trusted Signing (~$10/mo, `vpk pack --azureTrustedSignFile …`) or an OV
  cert (~$150–300/yr, `--signParams "<signtool args>"`). Sign exe + bundled `Update.exe` + `Setup.exe`.

## Phase 7 — Release automation (optional, later)

- A `release.ps1` (or GitHub Action) chaining publish → `vpk pack` → `vpk upload github`, versioned
  from a git tag.

---

## Sequencing & risks

- **Order:** Phase 0 → 1 → 2 → 3 can land as one "it installs and self-updates" milestone; Phase 4
  is the biggest single piece (new WPF wizard); Phase 5 layers AI on top; 6–7 are polish.
- **Risks:** single-file native-lib extraction (Phase 1) is the most likely surprise — validate
  early on a clean VM. Ollama silent-install UX (Phase 5) is the least controlled step; keep it
  fully skippable.
- **New dependency to approve:** `Velopack` NuGet + the `vpk` global tool.
