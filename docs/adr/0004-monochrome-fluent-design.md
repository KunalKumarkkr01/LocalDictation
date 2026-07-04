# ADR-0004: Monochrome, Windows 11 Fluent design language

- **Status:** Accepted (supersedes the earlier violet-tinted theme)
- **Date:** 2026-07-04

## Context
The initial UI used a violet-tinted dark theme. The owner wanted something more premium, restrained,
and native to Windows 11.

## Decision
Adopt a **monochrome black-and-white** palette: near-black grounds (`#0A0A0B`/`#141316`), warm
off-white text (`#F4F2EF`), white as the only accent — **no violet, no color**. Align typography,
spacing, materials (Mica-like), and rounded geometry to **Windows 11 Fluent**. The one deliberate
color exception is the transient processing state (ADR-0008).

## Consequences
- Calm, premium, native feel; brush keys in `Theme.xaml` are unchanged so the whole app restyles.
- Any new color must be justified against this decision (as ADR-0008 was).
