# CLAUDE.md — LocalDictation

Project-level instructions for Claude Code. These take precedence over global preferences
for this repository.

## Commit authorship

For **this project**, commits **should include a Claude co-author trailer**. This intentionally
overrides the global "never add Claude authorship markers" rule — the owner has opted in here.

End every commit message (and PR body) made in this repo with a trailing line:

```
Co-Authored-By: Claude <noreply@anthropic.com>
```

## Project summary

LocalDictation is a Windows 11 desktop app for system-wide, offline, AI voice dictation:
press a global hotkey (`Ctrl+Shift+Space`), speak, transcribe locally with Whisper, optionally
enhance with a local LLM (Ollama), and insert the text into the focused control.

- Stack: .NET 8, C#, WPF, Clean Architecture + MVVM.
- UI: monochrome (black & white), Windows 11 Fluent-aligned. The listening overlay is a small
  acrylic capsule at the bottom of the screen; the control panel follows the Win11 SettingsCard
  pattern. Design reference: `design/control-panel-design.html`.
- AI enhancement is **opt-in** (off by default) for a fast, verbatim default; the control panel's
  AI toggle drives the Ollama lifecycle behind the scenes.
- Full architecture: `implementation-plan.html`. Reproducible e2e test: `tests/e2e/run-e2e.ps1`.

## Verifying UI changes

Screenshots must use a **DPI-aware** process (the display is 200% DPI) or window positions won't
line up with capture coordinates. Always **clean-rebuild** the Desktop project when testing —
incremental WPF builds can leave a stale `.exe`.
