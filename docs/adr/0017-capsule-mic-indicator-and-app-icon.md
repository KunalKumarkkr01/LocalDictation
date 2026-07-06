# ADR-0017: Mic-mute indicator and real app icon on the listening capsule

- **Status:** Accepted
- **Date:** 2026-07-06

## Context
The capsule showed the target as **text only**, and gave no sign of the input device's state. A muted
microphone (a common hardware mic-mute key) delivers all-zero audio, which surfaced only as the generic
"No speech detected" after the fact — the single hardest dictation failure to self-diagnose.

## Decision
Two additions to the capsule (`OverlayWindow`):
- **Live mic-mute indicator** — a stroked mic icon that becomes a red mic-with-slash (`DangerBrush`)
  when the input device is muted. `IAudioCaptureService.IsInputMuted()` reads the selected/default
  capture endpoint's `AudioEndpointVolume.Mute`; `OverlayController` polls it on a 500 ms timer while
  the capsule is shown, so unmuting mid-session updates it. Indicator only — no toast, no block.
- **Real focused-app icon** — the actual app logo (Chrome, PowerShell, Windows Terminal, …) shown next
  to the name. `TargetControl.ExecutablePath` (from `Win32Inspector`) feeds `AppIconProvider`, which
  extracts the shell icon via `SHGetFileInfo` + `CreateBitmapSourceFromHIcon` (no System.Drawing
  dependency), cached per path, falling back to the `AppMark` when the path is unavailable
  (elevated/packaged apps).

## Consequences
- A muted mic is now obvious at a glance instead of a mystery empty transcript.
- The app icon is a deliberate, contained exception to the monochrome rule (ADR-0004) — it is the
  OS-provided logo, treated like a favicon; the mic icon stays within the palette
  (`TextSecondaryBrush` / `DangerBrush`). The capsule auto-widens (`SizeToContent`) to fit.
- Icon extraction is best-effort: any failure falls back to the app mark, never a blank.
