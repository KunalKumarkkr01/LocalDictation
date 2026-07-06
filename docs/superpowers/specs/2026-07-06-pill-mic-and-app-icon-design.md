# Pill: live mic-mute indicator + real focused-app icon — Design

**Date:** 2026-07-06
**Status:** Approved for implementation
**Owner:** Kunal

## Problem / goal
The listening capsule (`OverlayWindow`) shows the target app as **plain text only**. Two additions:
1. **Mic-mute indicator** — so a muted mic is instantly visible on the pill (the muted-mic bug that
   just cost a debugging session would have been obvious). Pill icon only: no toast, no block.
2. **Real focused-app icon** — show the actual app's logo (Chrome, PowerShell, Windows Terminal, …)
   next to its name, instead of just text.

Verify: dictate into Chrome → pill shows the Chrome logo + name. Mute the mic → pill shows a red
mic-slash icon; unmute mid-session → it flips back to the plain mic icon live.

## Non-goals
- No toast, no auto-unmute, no blocking when muted (pill indicator only — user's choice).
- No icon for the *control* (just the app). No animated mic icon (the waveform already shows activity).
- No tests (Windows-specific icon/endpoint APIs; a manual verify covers it).

## Feature 1 — Real focused-app icon
- **Domain:** add `string? ExecutablePath` to `TargetControl`.
- **Infrastructure:** `Win32Inspector.CaptureFocusedTarget` fills `ExecutablePath` from the pid via
  `Process.MainModule.FileName`, inside the existing defensive `GetProcessInfo` (returns null on the
  access-denied/elevated path, same probe already used for `IsElevated`).
- **Desktop:** new `AppIconProvider` — `ImageSource? ForExecutable(string? path)`:
  - `System.Drawing.Icon.ExtractAssociatedIcon(path)` → `Imaging.CreateBitmapSourceFromHIcon(...)`,
    frozen, **cached by path** (dictionary). Returns null on failure.
  - Consumer falls back to the shared `AppMark` DrawingImage when it returns null.
- **Pill:** add a 16×16 `Image x:Name="AppIcon"` immediately left of `TargetText`; new
  `OverlayWindow.SetTargetIcon(ImageSource)`. `OverlayController.Show` resolves the icon
  (`AppIconProvider.ForExecutable(target.ExecutablePath) ?? AppMark`) and sets it.

## Feature 2 — Live mic-mute indicator
- **Application port:** add `bool IsInputMuted()` to `IAudioCaptureService`.
- **Infrastructure:** `NAudioCaptureService.IsInputMuted()` reads the selected device's (or default)
  capture endpoint `AudioEndpointVolume.Mute` via `MMDeviceEnumerator` (already used for device
  enumeration). Defensive: returns false on any error (don't cry wolf).
- **Pill:** add a vector `Path x:Name="MicIcon"` between the waveform and the divider. Two geometries:
  plain mic vs mic-with-slash; new `OverlayWindow.SetMicMuted(bool)` toggles the geometry + colour
  (unmuted → `TextSecondaryBrush`; muted → `DangerBrush`).
- **Polling:** `OverlayController` injects `IAudioCaptureService`; while the capsule is visible it runs
  a ~500 ms `DispatcherTimer` calling `_window.SetMicMuted(_capture.IsInputMuted())`, started in
  `Show`, stopped in `Hide`. Also set once immediately on `Show` for no first-frame flicker.

## Architecture / files
- `Domain/TargetControl.cs` — `ExecutablePath`.
- `Application/Abstractions/WindowsAbstractions.cs` — `IAudioCaptureService.IsInputMuted()`.
- `Infrastructure/Windows/Win32Inspector.cs` — capture the exe path.
- `Infrastructure/Audio/NAudioCaptureService.cs` — `IsInputMuted()`.
- `Desktop/Services/AppIconProvider.cs` — new; exe → cached `ImageSource`.
- `Desktop/Services/OverlayController.cs` — resolve icon on Show; mute-poll timer; inject capture svc.
- `Desktop/Views/OverlayWindow.xaml` + `.xaml.cs` — `AppIcon` Image, `MicIcon` Path,
  `SetTargetIcon`, `SetMicMuted`.

## Error handling
- Icon extraction failure or null path → `AppMark` fallback (never blank).
- Mute query failure → treated as not-muted (no false red).
- Elevated/UWP targets → no exe path → fallback icon; unchanged dictation behaviour.

## Design language
Monochrome-safe: app icon is the OS-provided logo (the one deliberate colour exception, like a
favicon); mic icon uses existing `TextSecondaryBrush` / `DangerBrush`. Pill auto-widens via
`SizeToContent` and stays bottom-center (`PositionBottomCenter` recomputes from `ActualWidth`).

## Testing
Manual verify per goal (Chrome logo; mute→red slash; unmute→plain). No automated tests.
🧪 Test suggested: `AppIconProvider` returns non-null for a known exe and caches by path.
