# ADR-0006: Ollama lifecycle managed behind the AI toggle

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
AI enhancement needs Ollama running and the model loaded. Users shouldn't touch a terminal, and the
UI should reflect the (multi-second) startup honestly.

## Decision
Introduce `IOllamaLifecycle` / `OllamaLifecycle`. Flipping the control panel's AI toggle calls
`EnableAsync`: probe `/api/version`, start `ollama serve` if needed, then load the model with a tiny
`keep_alive` generate — emitting `StatusChanged` (Starting → LoadingModel → Ready/Failed) shown live
in the AI card. Disabling releases the model (`keep_alive: 0`).

## Consequences
- One toggle manages the whole local-LLM backend; status is transparent.
- `EnableAsync` is currently only invoked from `ControlPanelViewModel`, so Ollama is not started at
  app boot even when `AiEnabled` is true (known gap in CLAUDE.md).
