# ADR-0005: Control panel + history use the Windows 11 SettingsCard pattern

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
The first control panel used a single card with divider-separated rows (a Windows-10-era pattern)
that felt unnatural. Microsoft's own guidance and the Win11 Settings app use a cards-based system.

## Decision
Rebuild the **control panel** (replacing the old Settings window) and the **history window** to the
Win11 **SettingsCard** pattern: one rounded card per setting (header icon left, name + description,
control right), grouped under bold section headers, **immediate-apply (no Save button)**. Standard
40px title bar with app icon + minimize/close **caption buttons**, Mica-style gradient, 8px radius,
a thin overlay scrollbar. Shared styles in `Theme.xaml` (`Win11Card`, `GroupHeader`, `Switch`, etc.).

## Consequences
- Reads as a native Win11 app; control panel and history share one visual system.
- Settings apply on change; there is intentionally no confirm/save step.
