# ADR-0002: Fully local — Whisper.net + Ollama, no cloud

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
Dictation captures potentially sensitive speech across every app. Cloud STT/LLM adds latency,
cost, privacy risk, and an offline failure mode.

## Decision
Transcribe **on-device with Whisper.net** (whisper.cpp/GGML) and enhance with a **local LLM via
Ollama**. No network calls for core function. The `WhisperFactory` is loaded once and kept
resident; a lightweight processor is created per request to avoid model-reload latency.

## Consequences
- Full privacy and offline capability; predictable latency (base model ~1.9x RTF on the dev CPU).
- Model files are large and gitignored; users download them (`models/whisper/`, resolved via
  `AppPaths`/`LOCALDICTATION_MODELS`).
- Accuracy is bounded by the local model size the user's hardware can run.
