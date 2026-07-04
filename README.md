# 🎙️ LocalDictation

**System-wide, offline, AI voice dictation for Windows.** Press a global hotkey anywhere in Windows, speak, and have your words transcribed locally with Whisper, optionally refined by a local LLM, and inserted straight into whatever field has focus — Teams, Slack, Notion, VS Code, browsers, terminals, Word, anywhere.

No cloud. No accounts. No audio ever leaves your device.

> Built end-to-end from the technical design in [`implementation-plan.html`](implementation-plan.html) — a full architecture reference you can open in any browser.

---

## Why

Native Windows dictation is mediocre and cloud-tethered; commercial tools cost money and ship your audio to servers. LocalDictation delivers a ChatGPT-Voice-quality experience that works in **every** app while keeping every byte on-device.

## Core principles

- **Offline-first** — speech recognition and AI both run locally; no internet required.
- **Privacy-first** — nothing leaves the device by default; history & secrets encrypted at rest.
- **Edge-AI first** — tuned for CPU-only laptops (Intel i5/i7, Ryzen 5/7, 16 GB RAM, integrated graphics).
- **Extensible** — every subsystem (speech, AI, output, plugins, settings, history) sits behind an interface and is swappable via DI.

---

## Features

| | |
|---|---|
| 🎹 **Global hotkey** | `RegisterHotKey`-based activation from any foreground app (`Ctrl+Shift+Space` by default). |
| 🗣️ **Local Whisper** | Whisper.net (whisper.cpp) with resource-aware model selection (`base`/`small`). **0% WER** on the eval corpus. |
| 🧠 **Local LLM** | Optional Ollama post-processing: grammar fix, professional rewrite, translate, summarize, Markdown, custom prompts. Degrades gracefully if no LLM is installed. |
| 🎯 **Smart insertion** | Prioritised strategy chain (clipboard → SendInput → UIA) with clipboard save/restore and a floating-editor fallback. |
| 🔒 **Privacy guards** | Password/sensitive-field detection (UIA `IsPassword`), per-app blocklist, "never touch clipboard" mode. |
| 🕘 **History** | SQLite + FTS5 full-text search, favourites, retention pruning. |
| 🪟 **Polished UI** | Non-activating recording overlay with live mic meter, floating editor, settings & history windows — dark, violet-accented WPF. |

---

## Architecture

Clean Architecture + MVVM, dependencies pointing inward only (enforced by NetArchTest in CI).

```
src/
  LocalDictation.Domain            Entities, value objects, enums (no dependencies)
  LocalDictation.Application       Use cases + port interfaces (DictationPipeline, ISpeechEngine…)
  LocalDictation.Infrastructure    Adapters: Whisper, Ollama, NAudio, Win32/UIA, SQLite, plugins
  LocalDictation.Plugins.Abstractions   Public plugin SDK
  LocalDictation.Desktop           WPF shell: composition root, overlay, tray, settings, history
  LocalDictation.Shared            Result<T>, guards
  LocalDictation.Evals             Whisper WER/latency + LLM evaluation harness
tests/
  LocalDictation.UnitTests         Pipeline + persistence + primitives (xUnit + Moq)
  LocalDictation.Architecture.Tests  Clean-Architecture dependency rules (NetArchTest)
```

See the full design (diagrams, ADRs, roadmap, risk matrix) in **`implementation-plan.html`**.

---

## Getting started

### Prerequisites
- Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (Optional, for AI enhancement) [Ollama](https://ollama.com)

### Build & run
```powershell
git clone https://github.com/KunalKumarkkr01/LocalDictation.git
cd LocalDictation

# 1. Download speech models (Whisper base.en + small)
./scripts/setup-models.ps1

# 2. (Optional) pull a local LLM for AI enhancement
ollama pull phi3.5:3.8b-mini-instruct-q4_K_M

# 3. Build & run the tray app
dotnet run --project src/LocalDictation.Desktop
```
The app lives in the system tray. Press your hotkey anywhere and start talking.

### Models
Whisper `.bin` models are **not committed** (they're large). `scripts/setup-models.ps1` downloads them to `models/whisper/`. At runtime the app looks in `%AppData%/LocalDictation/models/whisper`, a repo-relative `models/whisper`, or the `LOCALDICTATION_MODELS` env var.

---

## Evaluation

The eval harness synthesizes known sentences via Windows TTS, runs them through the real Whisper engine, and measures accuracy + latency, then exercises the LLM path.

```powershell
dotnet run --project src/LocalDictation.Evals
```

Latest results on a Ryzen 9 8945HS (CPU-only):

| Model | WER | Avg latency | Real-time factor |
|---|---|---|---|
| Whisper `base` | **0.0%** | 2157 ms | **1.9×** |
| Whisper `small` | **0.0%** | 7047 ms | 0.6× |

LLM (Phi-3.5-mini) grammar/rewrite/format: ~400–800 ms warm. Full JSON report is written to `artifacts/eval-report.json`.

---

## Usage

Press **`Ctrl+Shift+Space`** to start recording, speak, and press it **again** to stop — the transcription inserts into the focused control (~3 s). `Esc` cancels. AI grammar/rewrite cleanup is **opt-in** (enable "Local AI" in Settings); by default you get fast, verbatim Whisper output.

## Tests

```powershell
dotnet test                     # 17 unit + architecture tests
pwsh tests/e2e/run-e2e.ps1      # full end-to-end: real audio -> mic -> Whisper -> insertion, read back
```

The unit suite covers pipeline behaviour (incl. graceful AI degradation), persistence round-trips, FTS search, retention, and Clean-Architecture dependency rules.

`run-e2e.ps1` drives the actual built app the way a user would: it plays a known sentence through the speaker so the **real microphone pipeline** captures it, presses the global hotkey to start/stop, and asserts the transcribed text lands in a real editable control (a `DictationSink` harness that mirrors its contents to disk). This verifies mic capture, VAD, Whisper, and clipboard/SendInput insertion end-to-end.

---

## License

MIT
