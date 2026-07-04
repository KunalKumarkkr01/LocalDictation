# ADR-0008: Single gold accent for the transient processing state

- **Status:** Accepted (deliberate exception to ADR-0004)
- **Date:** 2026-07-04

## Context
After the user stops speaking, the capsule spends a couple of seconds transcribing/inserting. In the
strict monochrome theme (ADR-0004) that transition was invisible — the pill looked identical to
listening. The owner explicitly asked for a color change plus animation to signal "working now".

## Decision
Introduce **one** transient accent — a soft gold `#E8B478` (`ProcessingBrush`) — used **only** for
the processing state, paired with an indeterminate traveling-wave shimmer. Listening stays white;
`OverlayController` maps Recording→white, Transcribing/Processing→gold shimmer, Error→red.

## Consequences
- Clear, legible state feedback without abandoning the monochrome identity.
- This is the sanctioned exception to "no color"; other color proposals still need their own justification.
