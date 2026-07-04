# ADR-0010: Leave the last dictation on the clipboard

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
Some targets (notably terminals, including Claude Code's) truncate a long pasted insertion. The
owner wanted a way to recover the full dictated text and paste it again.

## Decision
After a dictation is delivered, `DictationController` copies the final text onto the clipboard via
the UI dispatcher. This runs **after** the pipeline, deliberately overriding `ClipboardOutputTarget`'s
normal restore of the prior clipboard, so the dictated text is what remains.

## Consequences
- The last dictation is always re-pastable.
- The user's previous clipboard content is overwritten by design. Verified: dictating "…lazy dog."
  leaves exactly that text on the clipboard.
