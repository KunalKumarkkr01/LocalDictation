# ADR-0014: Distribute via Velopack with two-tier, download-on-first-run bootstrapping

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
LocalDictation now works well and is worth sharing. It needs a real distribution artifact instead
of a dev build launched from `bin/x64/Debug`. The payload is awkward: a .NET 8 WPF GUI plus heavy
assets — Whisper `.bin` models (~150 MB base.en, ~460 MB small) and, only when AI is enabled, Ollama
itself (~700 MB) and an LLM (~2 GB). Two shapes were considered and rejected:

- **NPX / npm** — wrong ecosystem. NPX runs Node CLIs; it cannot launch a compiled WPF window, and
  it would force users to install Node just to bootstrap a C# app.
- **One giant all-in-one installer (~3 GB)** — slow, no resume, re-downloads on every update, and
  forces the optional 2 GB LLM on users who only want fast verbatim dictation (violates ADR-0003).

The recommended pattern across the best-rated local-AI desktop apps (LM Studio, Ollama, GPT4All,
MacWhisper, SuperWhisper) is a **small bootstrapper + assets fetched on demand**.

## Decision
**Ship a lightweight Velopack installer and download the heavy assets at first run.**

**Installer / updater — Velopack** (v1.2.x). Chosen over Inno Setup and MSIX because it bundles
**auto-update** for free (important for a tool we keep iterating on), installs **per-user without
elevation**, and needs no code-signing cert to function. MSIX's Store/cert overhead and Inno's lack
of auto-update made them worse fits. Recorded API as of Velopack 1.2.0:

- Build: `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`, then
  `vpk pack --packId LocalDictation --packVersion <v> --packDir <publish> --mainExe LocalDictation.Desktop.exe --icon <app.ico> --outputDir Releases`.
- Runtime: `VelopackApp.Build().Run()` **must be the first line of an explicit `Main`** (WPF needs
  `EnableDefaultApplicationDefinition=false` + a hand-written `[STAThread] Main`). Updates via
  `UpdateManager(new GithubSource(repoUrl, null, false))` → `CheckForUpdatesAsync` →
  `DownloadUpdatesAsync` → `ApplyUpdatesAndRestart`.
- Hosting: **GitHub Releases** (`vpk upload github`) for now.

**Two tiers:**
1. **Tier 1 — the installer (~50–90 MB):** self-contained app + .NET runtime only. **No models,
   no Ollama.**
2. **Tier 2 — first-run acquisition:** a guided onboarding flow downloads the speech model on
   demand (reusing the existing `ISpeechModelManager.DownloadAsync`, which already streams from
   HuggingFace with progress + verification). The **LLM/Ollama stack is a separate, opt-in, later
   download** gated behind the AI toggle — never required to reach a working state.

**Asset location — assets live in app-data, never in the versioned app dir.** Velopack fully
replaces `%LocalAppData%\LocalDictation\current\` on every update and diffs it for deltas. Whisper
models, the SQLite history DB, logs, and `AppSettings` therefore live **outside** `current\` (e.g.
`%LocalAppData%\LocalDictation\models\`, `…\data\`). `AppPaths` (and the `LOCALDICTATION_MODELS`
override) must resolve there. This keeps downloaded models intact across updates and keeps
release/delta packages small.

**Auto-start via Velopack shortcuts, not a hand-rolled Run key.** Velopack's `--shortcuts …,Startup`
points through the stable install dir, which **fixes the ADR-known-gap dev-path HKCU Run key** that
currently breaks on rebuild.

## Consequences
- A small, shareable, auto-updating installer; per-user install with a stable exe path.
- First run requires a network connection to fetch the speech model (documented in onboarding);
  fully offline thereafter.
- Code signing is deferred: unsigned builds trigger a SmartScreen "unknown publisher" prompt.
  Acceptable for early sharing; the cheapest real fix is Azure Trusted Signing (~$10/mo) or an OV
  cert (~$150–300/yr). Tracked as a follow-up, not a blocker.
- `AppPaths` must be audited to guarantee no writes land under `current\`.
- Onboarding UX and logo/app-mark are specified in `docs/distribution-plan.md` and
  `design/onboarding-design.html`.
