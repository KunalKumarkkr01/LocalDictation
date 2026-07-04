# ADR-0011: Filter Whisper non-speech markers ([BLANK_AUDIO], …)

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
whisper.cpp emits bracketed annotations for non-speech — `[BLANK_AUDIO]`, `[ Silence ]`, `(music)`,
`♪…` — which were being inserted verbatim when a recording had no real speech.

## Decision
`WhisperNetEngine` skips any segment whose trimmed text is a wholly bracketed / parenthesized /
starred annotation, or is only musical notes/whitespace (`IsNonSpeechAnnotation`). If everything is
filtered, the transcript is empty and the controller shows "No speech detected" and inserts nothing.

## Consequences
- Silence no longer pastes `[BLANK_AUDIO]`.
- Whole-segment filtering only; a rare mixed segment ("[BLANK_AUDIO] hello") would keep the words.
  Pre-existing history rows created before this fix still contain the marker.
