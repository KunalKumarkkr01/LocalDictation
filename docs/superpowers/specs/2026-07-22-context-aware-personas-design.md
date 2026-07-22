# Context-Aware Persona System — Design Spec

Date: 2026-07-22
Status: Approved for planning
Scope decision: **Personas first.** Long-dictation resilience is researched and designed in full
here, but only its cheap safety subset is implemented in this deliverable; the chunk/merge engine
is deferred to its own spec.

---

## 1. Summary

Add a **persona** concept: a named, reusable LLM system-prompt that reshapes how a dictation is
enhanced, chosen automatically from the focused application, or manually from a picker palette.
Everything is data-driven — adding support for a new app is a config edit, never a code change.

Two invocation paths:

- **Auto** (primary hotkey, unchanged): when AI enhancement is on and auto-apply is enabled, the
  focused app's process name resolves to a persona (or a default fallback), and that persona's
  prompt drives the enhancement.
- **Picker** (new second hotkey): opens a searchable command-palette overlay of personas; the
  chosen persona applies to that one dictation and **force-enables AI enhancement for it**, even if
  the global AI toggle is off. This is the robust path for browser webmail and terminal-hosted
  coding agents, which auto-detection structurally cannot distinguish from their host process.

## 2. Goals / Non-goals

**Goals**
- Per-app persona prompts for Notion, Gmail/Outlook, Teams, and coding agents (Claude Code, Codex,
  Gemini CLI, Grok, Cursor, VS Code, etc.), plus a general fallback.
- Minimal-configuration UX that feels native to the existing Control Panel — no separate window.
- Auto-detection + manual picker, both first-class.
- View / add / edit / enable-disable / delete personas; default fallback; import/export; search.
- Portable, human-readable persona storage.
- Deliver full long-dictation research + a designed fallback strategy; implement the cheap safety
  subset now.

**Non-goals (this deliverable)**
- The full chunk/merge long-dictation engine (designed here, deferred to its own spec).
- Per-persona model or temperature overrides (extensibility hook only).
- Window-title / URL sub-app matching in the auto path (the picker covers these cases in v1).
- macOS bundle-id extraction (process-name matching is sufficient cross-platform).

## 3. Research findings (established before design)

The current pipeline is already persona-ready:

- `TargetControl` (focused app) is captured at hotkey-press **before** transcription and is already
  passed into `DictationPipeline.RunAsync(clip, target, settings, ct)` — but is ignored for prompt
  selection. Today prompt selection is a single line: `mode = AiEnabled ? DefaultMode : None`.
- The system prompt is a hardcoded `switch` in `PromptTemplates.SystemPrompt(mode, lang)`.
  `ITextProcessor.ProcessAsync` **already accepts an optional `customPrompt`** (used only by
  `ProcessingMode.Custom`).
- The one identifier populated on **both** Windows and macOS — even for Electron/Chromium apps —
  is the **executable name** (`TargetControl.ProcessName`, casing differs on macOS). The pid
  (`WindowHandle` on Mac) is per-launch and unusable as a key. There is no bundle-id captured today.
- Browser tabs and terminal-hosted agents cannot be told apart from their host process by process
  identity alone; the only in-process discriminator is `WindowTitle` (fragile). → the picker exists.
- Settings persistence precedent: `JsonSettingsStore` (System.Text.Json, `WriteIndented`,
  `PropertyNameCaseInsensitive`, **PascalCase on disk**, atomic temp-write + `File.Move`,
  `SchemaVersion` migration, graceful fallback to defaults). `AppSettings.BlockedApps` is an
  existing per-app `List<string>` precedent.
- UI precedent: `Win11Card`/`GroupHeader`/`CardTitle`/`CardDesc`/`Switch`/`ComboBoxStyle`/
  `TextBoxStyle`/`GhostButton`/`PrimaryButton` styles; the System-status `ItemsControl`-of-cards for
  a list; `HistoryWindow` for list-rows-with-actions + search + empty-state; `FloatingEditorWindow`
  for multiline text editing; the AI card's reveal-on-toggle for inline expansion. VMs are
  hand-written `SetProperty` (the MVVM source-generator gotcha). Immediate-apply via `Persist()`.
- Ollama: default model `phi3.5:3.8b`; call is `/api/chat`, non-streaming; `Temperature = 0.2`
  hardcoded; **no `num_ctx`, no chunking, no length guard**; default `HttpClient.Timeout` = 100s.
  Failure falls back to the raw transcript everywhere except one edge where an HTTP-timeout
  cancellation can propagate instead of degrading to raw.

## 4. Data model

```
Persona
  Id                 stable slug, e.g. "notion", "email", "coding-agent"
  Name               display name
  Glyph              optional icon hint (built-ins ship a vector glyph; user personas fall back to AppMark)
  MatchProcessNames  List<string> — normalized (lowercased, ".exe" stripped) exe names that AUTO-trigger
                     this persona. Empty = never auto-matches (picker-only).
  SystemPrompt       the persona instruction, sent as the LLM system message
  Enabled            bool
  IsBuiltIn          bool — built-ins can be edited/disabled but not deleted (offer "Reset")
```

```
PersonaSettings   (personas.json)
  SchemaVersion     int, forward-migration
  AutoApply         bool (default true) — master switch for auto-detection
  DefaultPersonaId  string? — fallback when AI on + auto-apply + no match (default "general")
  PickerHotkey      string — second global hotkey (default "Ctrl+Alt+Space")
  Personas          List<Persona>
```

Types live in the Domain/Application layers (Domain `Persona` entity; `PersonaSettings` as an
Application configuration type alongside `AppSettings`), keeping the portable core platform-neutral.

### Seeded built-ins (first run)
- **General cleanup** — default fallback; grammar/punctuation, verbatim-preserving. `MatchProcessNames: []`.
- **Notion** — clean Markdown (headings, bullets, tables, quotes, callouts, doc style). `["notion"]`.
- **Email** — professional email (greeting, structured body, closing, tone/grammar). `["outlook"]`;
  webmail reached via picker.
- **Teams** — short conversational messages, remove filler, friendly-professional. `["ms-teams","teams"]`.
- **Coding Agent** — transform speech into a high-quality implementation prompt (organize
  requirements, preserve technical detail verbatim, clarify intent, prompt-engineering quality).
  `MatchProcessNames: []` (picker-only — no reliable host-process signal).

## 5. Storage

New `personas.json`, sibling of `settings.json` under the app data root (add `PersonasFile` to
`AppPaths`). New `IPersonaStore` mirroring `JsonSettingsStore` exactly (System.Text.Json,
`WriteIndented`, PascalCase, atomic temp-write + `File.Move`, `SchemaVersion` migration, graceful
fallback that re-seeds built-ins on read failure). Kept separate from `settings.json` because the
file **is** the import/export format, it keeps settings lean, and it isolates a larger list-shaped
schema with its own migration.

## 6. Persona resolution

New `IPersonaResolver` (Application):

```
Persona? ResolveForTarget(TargetControl target, PersonaSettings personas)
```

Normalize `target.ProcessName` (lowercase, strip `.exe`); return the first **enabled** persona whose
`MatchProcessNames` contains it; else the configured default persona (if enabled); else null (→ fall
back to today's `settings.DefaultMode` behavior).

### A persona never gates whether AI runs

A persona only selects *which prompt* the AI enhancement uses; it is never a precondition for
enhancement. When AI is on, the resolution ladder is:

1. **Matched persona** (auto path) or **picked persona** (picker path), or
2. else the **default persona**, if enabled, or
3. else the existing global **Cleanup mode** (`settings.DefaultMode`, e.g. Grammar correction).

So "AI on but no persona enabled/matched" produces generic cleanup, **not** raw text. Raw text is
delivered only when Ollama itself fails (the existing graceful-degradation safety net) — that is
unrelated to persona resolution. The picker path additionally force-enables AI for one dictation
even when the global toggle is off.

### Relationship to the existing "Cleanup mode" control

The AI-enhancement card already has a "Cleanup mode" ComboBox bound to `settings.DefaultMode`. To
avoid two overlapping "what prompt by default" controls:

- When **Apply personas automatically** is ON, the resolved/default persona drives enhancement and
  the legacy "Cleanup mode" is not consulted for auto dictations (it may be hidden or shown as
  "Managed by personas").
- When **Apply personas automatically** is OFF, behavior is exactly as today — "Cleanup mode"
  (`DefaultMode`) drives enhancement — and personas apply only via the picker. This keeps full
  backward compatibility for users who never touch personas.

## 7. Pipeline integration (minimal surgery)

- `DictationPipeline.RunAsync` gains optional `Persona? personaOverride = null`. Prompt selection at
  the current mode-selection line becomes:
  1. `personaOverride != null` → use it (picker path; AI forced on for this call).
  2. else `AiEnabled && personaSettings.AutoApply` → `resolver.ResolveForTarget(target, personas)`.
  3. else existing behavior (`settings.DefaultMode`).
- `ITextProcessor.ProcessAsync` gains optional `string? systemPromptOverride`. When set, it is used
  **directly** as the system message and the raw transcript as the user message, bypassing the
  `PromptTemplates` switch. `OllamaTextProcessor` and `NoOpTextProcessor` each take the one-line
  addition. Optional param → no breaking change; graceful raw-fallback path is untouched.
- The resolved persona name is surfaced to the capsule overlay (e.g. "Notion persona") and recorded
  on the history entry for transparency.

## 8. Invocation & hotkeys

- `IHotkeyService` extended to register a **second** hotkey and raise an event identifying which
  fired. `HotkeyService` (Win32 `RegisterHotKey`, distinct id) and `CarbonHotkeyService` (macOS)
  both register the picker chord.
- **Primary hotkey**: unchanged flow; pipeline auto-resolves the persona.
- **Picker hotkey**: capture `TargetControl` (as today), then show the **persona picker overlay** —
  a focusable acrylic command-palette window listing enabled personas: type-to-filter, ↑/↓ + Enter
  or number keys to select, ESC to cancel. On selection, a dictation session starts bound to that
  persona with AI forced on for the session; a second primary/picker press stops and sends; output
  routes to the pre-captured target (clipboard fallback always applies). The overlay reuses the
  existing acrylic/overlay design language.

## 9. UI — "Personas" section in the Control Panel

Placed directly after "AI enhancement" (personas are an AI concept). Built only from existing styles.
No new tray-reachable window (honors the constraint). Implemented in both WPF (`Win11Card`) and
Avalonia (`Classes="card"`), driven by the same-shaped VM.

- **Header config card:** `Apply personas automatically` (Switch) · divider · `Default persona`
  (ComboBox) · divider · `Persona picker hotkey` (chord capture, validated against the primary
  hotkey; duplicates rejected).
- **Persona list:** `ItemsControl` of `Win11Card` rows (System-status pattern) — glyph badge · Name
  (+ Default/Custom/Editing pill) · match summary ("Auto · notion.exe", "Picker only") · Enabled
  Switch · **Edit** (GhostButton) · Delete (user personas only). A search box appears when the list
  grows (HistoryWindow pattern). "**+ Add persona**" is right-aligned on the section header.
- **Inline editor (no separate window):** Edit expands the card in place (AI-card reveal pattern) to
  show Name, auto-match apps (comma-separated exe names, blank = picker-only), a multiline prompt
  `TextBox` with a **live character counter**, an Enabled toggle, and Cancel / Save.
- **Import… / Export…** GhostButtons in the section footer (Win32 file dialogs; Avalonia
  `StorageProvider`), reading/writing the `personas.json` schema with validation.
- Immediate-apply via the existing `Persist()` idiom; hand-written `SetProperty` VMs;
  `ObservableCollection<PersonaRowViewModel>`.

## 10. Persona prompt-size recommendation

Ollama's default context window is small (2048 tokens on long-standing defaults, 4096 on newer
builds) unless `num_ctx` is set, and the system prompt competes with the transcript for that budget.

- **Soft limit ~1,500 characters (~400 tokens)** per persona prompt; the editor's live counter warns
  past that. Hard cap ~4,000 chars. This leaves the bulk of the window for the actual dictation even
  on the smallest default context.

## 11. Long-dictation research (delivered) + fallback strategy

### Findings (default `phi3.5:3.8b` q4, local)

| Dimension | Reality |
|---|---|
| Context window | Ollama default `num_ctx` 2048–4096 tokens (not the model's 128K max, which is gated by `num_ctx`, never set by the app). ~4096 tokens ≈ ~16K chars ≈ ~2,700 words ≈ ~18 min at ~150 wpm. |
| Failure mode | **Silent truncation** — Ollama drops the oldest tokens, so a long enhancement quietly omits the start. Not a crash. This is the real risk. |
| Timeout | Default `HttpClient.Timeout` = 100s. Long CPU-only enhancement can exceed it, surfacing as cancellation; an edge exists where that cancellation propagates instead of falling back to raw. |
| Memory | phi3.5 q4 resident ≈ 2.3–3 GB; KV-cache grows ~linearly with `num_ctx`. |
| Latency | ~20–60 tok/s CPU (faster on GPU); a few-thousand-token pass = tens of seconds. |

Exact `num_ctx`/`num_predict` defaults vary by Ollama version and will be pinned during the deferred
chunk-engine spec.

### Implemented NOW (cheap, high-value safety subset)
1. Set explicit **`num_ctx` (default 8192, configurable)** on the enhancement request so mid-length
   dictations stop silently truncating.
2. Give the enhancement HTTP client an explicit **generous timeout (~5 min)** instead of 100s.
3. **Pre-flight size estimate** (`chars/4`); past the safe budget, a **non-blocking overlay hint**
   warns the dictation may exceed the model and will be delivered verbatim if enhancement fails.
4. **Harden raw-transcript preservation** on the timeout-cancellation edge so AI failure always
   degrades to raw (already seeded + on clipboard + in history — this closes the one gap).

### Designed NOW, DEFERRED to its own spec (full engine)
Sentence/paragraph-boundary splitting under the token budget → per-chunk enhancement with the same
persona prompt → merge (concat + optional light seam pass) → capsule progress ("Enhancing 2/4") →
retry-with-backoff → best-effort partial delivery on repeated failure. A genuinely separate subsystem;
bundling it would delay persona value and enlarge the blast radius. The user never loses work — the
raw transcript is preserved throughout.

## 12. Edge cases

- Persona matches but AI off (primary path) → verbatim; documented (personas require AI).
- Ollama not ready when picker forces AI → "Starting AI…" state; if it can't start, fall back to
  verbatim + notify; never block.
- Picker steals focus → target captured before the picker is shown; clipboard fallback always.
- Sensitive/blocked field → already blocked upstream, before persona logic; unaffected.
- macOS process-name casing ("Google Chrome") → lowercase-normalize; Electron desktop apps still
  match by exe.
- Default persona deleted/disabled → reset default to General cleanup.
- Imported `personas.json` → validate schema; cap prompt length; re-slug on id collision.
- Picker hotkey equals primary hotkey → rejected in the editor with a clear message.

## 13. Extensibility

New app = new persona row (zero code). Future, non-blocking: optional window-title keyword rules
(site/tab-level), per-persona model/temperature, first-encounter "create a persona for this app?"
prompt, community persona import, the deferred long-dictation chunk engine.

## 14. Testing (suggested, not exhaustive)

- `PersonaResolver` unit tests: exact match, disabled skip, default fallback, no-match → null,
  macOS-casing normalization.
- `IPersonaStore` round-trip + migration + corrupt-file re-seed.
- `DictationPipeline` mode-selection branch tests (override / auto / neither).
- Hotkey-duplicate validation.

## 15. Production-readiness recommendations (open-source release)

- Ship a small **curated persona library** (the built-ins above) and document the `personas.json`
  format in the docs site so contributors can PR new personas.
- Add an ADR for the persona architecture and the auto-vs-picker decision (fits the existing
  `docs/adr/` series).
- Document the two hotkeys and the "personas require AI" rule in onboarding/README.
- Keep persona prompts as data, not code, so community contributions never touch the binary.
- Redact persona names from any telemetry (there is none today; keep it that way).

## 16. Delivery phases

1. Storage + model (`Persona`, `PersonaSettings`, `IPersonaStore`, `AppPaths.PersonasFile`, seed).
2. Resolver + pipeline/text-processor integration (auto path).
3. Second hotkey + picker overlay (picker path, AI-force-on).
4. Control Panel Personas section — WPF, then Avalonia mirror.
5. Import/export.
6. Long-dictation safety subset (num_ctx, timeout, pre-flight hint, raw-preservation hardening).
7. Docs/ADR + built-in persona library polish.

Each phase is a focused, self-contained commit (per project convention), with a
`Co-Authored-By: Claude <noreply@anthropic.com>` trailer.
