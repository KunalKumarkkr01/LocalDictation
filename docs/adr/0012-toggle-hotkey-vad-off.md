# ADR-0012: Toggle hotkey; VAD auto-stop off by default

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
Early builds experimented with push-to-talk and aggressive silence auto-stop, which felt jittery and
chopped speech during natural pauses.

## Decision
Interaction is a simple **toggle**: press `Ctrl+Shift+Space` to start, press again to send; ESC
cancels. A single-slot guard prevents overlapping sessions. VAD **auto-stop is off by default**;
when enabled it requires both a minimum recording length and a sustained trailing silence.

## Consequences
- Predictable, calm interaction that never cuts mid-sentence.
- Global hotkey uses `RegisterHotKey` + `ComponentDispatcher.ThreadPreprocessMessage`, with fallback
  combos if the preferred one is unavailable (`Ctrl+Win+Space` is reserved by Windows).
