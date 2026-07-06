# ADR-0016: Open the glass fallback editor when focus moves away

- **Status:** Accepted
- **Date:** 2026-07-06

## Context
The target window/control is captured when the hotkey fires, but insertion happens seconds later when
recording stops. If the user clicks away or alt-tabs in between, the router still attempted insertion —
`SetForegroundWindow` + synthesized paste/keystrokes could land the dictated text in the **wrong app**.
A `FloatingEditorWindow` already existed as a fallback, but only for sensitive/elevated targets or when
every strategy failed — not for focus drift — and it was a plain small card, not the app's glass look.

## Decision
- **Trigger on focus drift.** At delivery, `OutputRouter` compares the live foreground window
  (`GetForegroundWindow`) to the handle captured at trigger time. If they differ, it opens the editor
  with `EditorReason.FocusMoved` instead of inserting. Gated by `AppSettings.EditorOnFocusLoss`
  (**on by default**); existing sensitive/elevated/all-failed fallbacks keep their own reasons.
- **Glass redesign.** `FloatingEditorWindow` is reworked as a translucent dark-glass window (the pill's
  semi-transparent fill) with the standard Win11 chrome shared by the control panel: 40px title bar
  with the app mark, and minimize / maximize-restore / close caption buttons (manual work-area bounds
  so a borderless transparent window doesn't cover the taskbar). It shows the transcript in an editable
  text box with Copy and Close, plus a sub-label saying why it opened.

## Consequences
- Dictated text is never silently typed into the wrong window; if focus moved, it surfaces in an
  editor the user can edit, copy (it's also on the clipboard) and manage like a normal app window.
- Window-level comparison (not control-level UIA) is deliberate: robust and cheap, and a moved
  foreground window is the case that matters. Power users can disable the guard in Settings.
