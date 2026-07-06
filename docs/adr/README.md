# Architecture Decision Records

Short records of the significant decisions behind LocalDictation and *why* they were made, so the
rationale survives across sessions. Format: Context → Decision → Consequences. Newest decisions may
supersede older ones; each notes its status.

| # | Decision | Status |
|---|---|---|
| [0001](0001-clean-architecture-mvvm.md) | Clean Architecture + MVVM on .NET 8 WPF | Accepted |
| [0002](0002-local-first-whisper-ollama.md) | Fully local: Whisper.net + Ollama, no cloud | Accepted |
| [0003](0003-ai-enhancement-opt-in.md) | AI enhancement is opt-in (off by default) | Accepted |
| [0004](0004-monochrome-fluent-design.md) | Monochrome, Windows 11 Fluent design language | Accepted |
| [0005](0005-win11-settingscard-pattern.md) | Control panel + history use the Win11 SettingsCard pattern | Accepted |
| [0006](0006-ollama-lifecycle-behind-ai-toggle.md) | Ollama lifecycle managed behind the AI toggle | Accepted |
| [0007](0007-frequency-reactive-waveform.md) | Frequency-reactive waveform via FFT + CompositionTarget.Rendering | Accepted |
| [0008](0008-gold-processing-state.md) | Single gold accent for the transient processing state | Accepted |
| [0009](0009-drop-live-text-preview.md) | Drop the live text preview feature | Accepted |
| [0010](0010-clipboard-last-dictation.md) | Leave the last dictation on the clipboard | Accepted |
| [0011](0011-filter-nonspeech-markers.md) | Filter Whisper non-speech markers ([BLANK_AUDIO], …) | Accepted |
| [0012](0012-toggle-hotkey-vad-off.md) | Toggle hotkey; VAD auto-stop off by default | Accepted |
| [0013](0013-claude-coauthor-trailer.md) | Enable a Claude co-author trailer for this repo | Accepted |
| [0014](0014-velopack-distribution.md) | Distribute via Velopack; two-tier, download-on-first-run | Accepted |
| [0015](0015-accurate-failure-reasons-and-diagnostics.md) | Accurate failure reasons + readiness checks and a self-test | Accepted |
| [0016](0016-glass-editor-on-focus-loss.md) | Open the glass fallback editor when focus moves away | Accepted |
| [0017](0017-capsule-mic-indicator-and-app-icon.md) | Mic-mute indicator and real app icon on the listening capsule | Accepted |
