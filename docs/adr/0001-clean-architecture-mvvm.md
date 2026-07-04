# ADR-0001: Clean Architecture + MVVM on .NET 8 WPF

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
A Windows-native, offline dictation app needs deep OS integration (global hotkey, SendInput, UI
Automation, tray) but also a testable core that can swap speech/LLM backends.

## Decision
Use **.NET 8 + WPF** with **Clean Architecture**: `Domain` (pure entities) ← `Application`
(abstractions + pipeline + settings) ← `Infrastructure` (Windows/impl) and `Desktop` (WPF/MVVM
shell). Dependencies point inward; UI and OS concerns sit at the edges behind interfaces.
View models are MVVM with **hand-written properties** (no source-generator attributes).

## Consequences
- Backends (Whisper, Ollama, output targets) are swappable behind interfaces; the core is unit- and
  NetArchTest-testable.
- WPF markup compiler conflicts with CommunityToolkit.Mvvm generators, so we forgo `[ObservableProperty]`.
- Windows-only; a cross-platform port would replace the Infrastructure + Desktop layers.
