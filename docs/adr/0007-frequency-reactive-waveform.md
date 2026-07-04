# ADR-0007: Frequency-reactive waveform via FFT + CompositionTarget.Rendering

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
The capsule waveform originally scaled a fixed bell curve by overall RMS, so every bar moved
together — it didn't feel alive. The owner wanted it to react to the actual voice frequencies.

## Decision
`NAudioCaptureService` runs a **512-point Hann-windowed FFT** per audio buffer and folds it into
**13 log-spaced bands** (~80 Hz–4 kHz), emitting `SpectrumChanged`. `OverlayWindow` eases each bar
toward its band (fast attack, slower release) on a **`CompositionTarget.Rendering`** loop (frame-
synced ~60 fps). The dot also scales gently with overall level.

## Consequences
- The waveform genuinely dances with the voice spectrum.
- **Two bugs were fixed here, worth remembering:** NAudio's FFT already scales by 1/N, so an extra
  `2/N` factor double-normalized magnitudes to ~zero (bars stuck flat) — removed it and set
  `SpectrumGain` accordingly. And a `DispatcherTimer` at Background priority was starved to ~19 fps —
  `CompositionTarget.Rendering` fixed the frame rate.
