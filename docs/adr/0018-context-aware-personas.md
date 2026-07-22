# ADR-0018: Context-aware personas

- **Status:** Accepted
- **Date:** 2026-07-22

## Context
AI enhancement (ADR from the original design) was a single global setting: on/off plus one
`ProcessingMode` (grammar fix, professional rewrite, translate, summarize, Markdown, or a custom
prompt) applied identically everywhere. In practice the "right" cleanup depends on where the text is
going — a Notion doc wants structured Markdown, an email wants a greeting and a closing, a Teams
message wants something short and conversational, a prompt for a coding agent wants precise
technical language preserved verbatim. Users had to manually flip modes per app, or accept one
prompt for everything.

Two more gaps: the four legacy `ProcessingMode` prompts (`PromptTemplates`) were hardcoded strings,
not editable without a rebuild; and there was no way to force AI on for a single dictation without
flipping the global toggle (and remembering to flip it back).

## Decision
Personas are user-editable data, not code — a named system prompt with optional auto-match rules,
stored in `personas.json` (a sibling of `settings.json`, same format used for import/export).

- **Two hotkeys.** The primary toggle (`Ctrl+Shift+Space`, unchanged) starts/stops dictation and
  applies whichever persona auto-resolves, but only when AI enhancement is on. A second hotkey
  (`Ctrl+Shift+P`, `PersonaSettings.PickerHotkey`, live-rebindable in Settings) opens a searchable
  persona palette — the way to reach browser webmail and terminal-hosted coding agents that
  process-name detection can't identify. The palette is **gated on `IOllamaLifecycle.Status`**: it
  only opens when the model is `Ready`; when AI is off / starting / loading / failed it shows a
  state-specific notification and does not open, so a persona pick never lands in a silent,
  feedback-less wait on a not-ready model. Enabling AI (and the model download) stays a deliberate
  Settings action — auto-starting Ollama from a hotkey was intentionally not done (see Consequences).
- **Personas never gate whether AI runs — only which prompt it uses.** `PersonaResolver.Decide`
  ladder: an explicit picker override wins outright; otherwise, if AI is off, no persona applies at
  all (raw transcript); if AI is on, auto-apply matches the focused app's process name
  (`Persona.MatchProcessNames`, normalized lowercase + `.exe` stripped so Windows and macOS compare
  equal) against enabled personas, falling back to the configured default persona, or — with
  auto-apply off — the legacy mode's persona. "AI on, no persona resolved" still means generic
  cleanup (the `general` System persona), never untouched raw text.
- **The four legacy `ProcessingMode` prompts became editable System personas** (`general`,
  `professional`, `summarize`, `markdown`, `PersonaKind.System`) instead of hardcoded strings —
  `PersonaIdForMode` maps each mode to its persona id so existing behavior for auto-apply-off users
  is unchanged, just now editable. Every persona — System, BuiltIn, or User — is editable in the
  control panel, with a one-click **Reset to default** (`PersonaSeeds.DefaultPromptFor`) for
  System/BuiltIn kinds; only User personas are deletable.
- **Curated built-in app personas** (`PersonaKind.BuiltIn`, seeded by `PersonaSeeds`): Notion, Email
  (`outlook`), Teams (`ms-teams`/`teams`), and a picker-only Coding Agent persona (no
  `MatchProcessNames` — terminal-hosted coding agents share a process name with the terminal itself,
  so auto-detection can't distinguish them; same reasoning covers browser-tab webmail, which the
  picker also has to cover manually).
- **Storage doubles as the share format.** `JsonPersonaStore` loads/saves `personas.json`
  atomically (temp file + rename), seeding factory defaults on first run or an unreadable file.
  Export just serializes the current `PersonaSettings`. **Import merges, it never replaces:** new
  ids are added as `PersonaKind.User`; an id that already exists is only updated if the *existing*
  persona is `User`-kind (name/prompt/match-list/enabled overwritten); a System or BuiltIn seed is
  never touched by import regardless of what an imported file contains. Re-importing a shared file
  or a stale export is therefore always safe — it can add or refresh your own custom personas, it
  cannot corrupt or reset the built-ins.

**Long-dictation safety subset.** AI enhancement now requests a larger context window (`num_ctx`)
and a longer HTTP timeout (`AppSettings.EnhancementTimeoutSeconds`) from Ollama, and — as before —
degrades to the raw transcript on any enhancement failure (timeout, model error, Ollama down), so a
long dictation never loses the transcribed text even if the LLM can't process it in time. This is a
safety net, not a length feature: a full chunk/merge engine for genuinely long (20–30 minute)
dictations is a deliberate follow-up, not part of this change — see "Known follow-ups" below.

## Consequences
- Getting the right tone per app is now zero-effort for the four curated apps, and one hotkey
  press away for anything else, without ever having to touch the global AI toggle for a one-off.
- The prompt surface area is now entirely data — adding an app persona is a `PersonaSeeds` entry
  (or a user-authored one, no code change), and every prompt, including the ones that used to be
  hardcoded, is inspectable and editable in the control panel.
- Process-name matching is a coarse signal: it can't distinguish web apps sharing a browser process
  or coding agents sharing a terminal process. The picker exists specifically to cover that gap
  rather than chasing window-title heuristics, which was judged not worth the fragility.
- Import/export being a strict merge (never overwriting seeds) trades a small amount of flexibility
  (you can't use import to *reset* a System/BuiltIn prompt someone else customized) for a much
  larger safety property: sharing or re-applying a persona pack is always non-destructive.
- **Known follow-ups (explicitly out of scope):** the full long-dictation chunk/merge engine (its
  own spec); window-title-based sub-app matching; per-persona model/temperature overrides; a visible
  in-overlay banner for the oversized-transcript hint (currently log-only).
