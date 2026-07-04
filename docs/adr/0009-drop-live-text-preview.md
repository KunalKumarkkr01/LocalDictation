# ADR-0009: Drop the live text preview feature

- **Status:** Accepted (feature built, then reverted)
- **Date:** 2026-07-04

## Context
We designed and fully implemented an opt-in "live text preview": while speaking, a background loop
transcribed the growing audio every ~0.7s with the fast **tiny** Whisper model and streamed rolling
words into a glass line above the capsule (final insert still used the accurate model). It worked.

## Decision
**Remove it.** On trial the owner found it unnecessary and it added latency and moving parts (a
second model, a capture snapshot API, an overlay preview line, a preview loop) for little value.

## Consequences
- Reverted entirely: `AppSettings.LivePreview`, the control-panel "Live text preview" card + VM
  binding, `ILivePreviewTranscriber`/`WhisperLivePreview`, `IAudioCaptureService.Snapshot`, the
  overlay preview line, and the preview loop; the `ggml-tiny.bin` model was deleted.
- **Do not re-add without an explicit request.** The design mockup still shows the concept for history.
