# ADR-0003: AI enhancement is opt-in (off by default)

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
LLM cleanup (grammar, punctuation) improves text but adds several seconds of latency and requires
loading a multi-GB model. Most quick dictations want instant, verbatim output.

## Decision
Ship with **AI enhancement OFF by default** (`AppSettings.AiEnabled = false`). The default path is
fast Whisper-only transcription. The control panel's AI toggle turns it on, which drives the Ollama
lifecycle (ADR-0006).

## Consequences
- Fast, dependable default; the heavy LLM path is a deliberate choice.
- The LLM stack is not started at app boot (see the reboot gap in CLAUDE.md); enabling AI currently
  starts Ollama only when the control panel opens.
