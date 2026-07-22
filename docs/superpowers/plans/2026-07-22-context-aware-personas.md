# Context-Aware Persona System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let dictation adopt a per-application "persona" (an editable LLM system-prompt) resolved automatically from the focused app, or chosen manually from a picker palette, with every enhancement prompt user-editable.

**Architecture:** A `Persona` is data (id, name, match process-names, system prompt, enabled, kind), stored in a `personas.json` sibling of `settings.json` via a `JsonPersonaStore` that mirrors the existing `JsonSettingsStore`. A pure `PersonaResolver.Decide(...)` maps the focused `TargetControl` (already captured before transcription) — or an explicit picker override — to a `PersonaDecision` (whether to enhance, which mode, which system prompt). `DictationPipeline` consumes that decision and passes the resolved system prompt through a new `systemPromptOverride` parameter on the existing `ITextProcessor.ProcessAsync`. A second global hotkey opens a persona-picker overlay that force-enables AI for one dictation. The Control Panel gains a "Personas" section built from existing Win11-card styles.

**Tech Stack:** .NET 8, C#, Clean Architecture + MVVM; WPF (Windows) + Avalonia (macOS); System.Text.Json; Ollama (`/api/chat`); xUnit + Moq; NetArchTest.

## Global Constraints

- **Commit trailer:** end every commit message with `Co-Authored-By: Claude <noreply@anthropic.com>` (this project opts in; overrides the global no-attribution rule).
- **Commit style:** focused, phased commits — one logical change each.
- **SDK pin:** `global.json` pins SDK `8.0.417`; keep the classic `.sln` (do not create `.slnx`).
- **Portable core stays platform-neutral:** `Domain`, `Application`, `Infrastructure` are plain `net8.0` — no OS-specific APIs. Windows-only code goes in `Infrastructure.Windows`/`Desktop`; macOS-only in `Infrastructure.Mac`/`Desktop.Avalonia`.
- **MVVM generator gotcha:** view models use hand-written `SetProperty` properties, NOT `[ObservableProperty]`/`[RelayCommand]` generators (they collide with the WPF markup compiler → CS0102). Follow the existing `ControlPanelViewModel` style.
- **Settings persistence conventions:** System.Text.Json, `WriteIndented = true`, `PropertyNameCaseInsensitive = true` (⇒ PascalCase on disk), atomic temp-file + `File.Move(overwrite: true)`, `SchemaVersion` forward-migration, graceful fallback on read failure.
- **Design language:** monochrome only. Tokens (from `Themes/Theme.xaml`): `Bg #0A0A0B`, `Surface #141315`, `SurfaceRaised #1C1B1E`, `SurfaceHover #232227`, `Border #2B2A2F`, `BorderSoft #1F1E22`, `TextPrimary #F4F2EF`, `TextSecondary #9D9B97`, `TextFaint #66645F`, `Accent #EDEBE7`, `Processing #E8B478` (gold — active/working only). Reuse styles: `Win11Card`, `GroupHeader`, `CardTitle`, `CardDesc`, `Switch`, `ComboBoxStyle`, `TextBoxStyle`, `GhostButton`, `PrimaryButton`. No new colors.
- **Test/build commands (Windows dev box):**
  - Build: `dotnet build LocalDictation.sln -c Debug --nologo`
  - Test: `dotnet test LocalDictation.sln --nologo` (17 tests today)
  - Never `PublishSingleFile`; never `taskkill //IM LocalDictation.exe` (kills the user's installed app).
- **macOS parts compile-check only on Windows** — `Infrastructure.Mac`/`Desktop.Avalonia` build by project, and the mac controller/overlay are verified on Windows by building the projects, not run. Note this in mac tasks.

---

## File Structure

**Create**
- `src/LocalDictation.Domain/Persona.cs` — the `Persona` entity + `PersonaKind` enum (or add enum to `Enums.cs`).
- `src/LocalDictation.Application/Configuration/PersonaSettings.cs` — persisted persona config object.
- `src/LocalDictation.Application/Processing/PersonaSeeds.cs` — factory defaults (System + BuiltIn personas) + mode↔persona-id mapping + reset lookup.
- `src/LocalDictation.Application/Abstractions/IPersonaStore.cs` — persona persistence port.
- `src/LocalDictation.Application/Abstractions/IPersonaResolver.cs` — `IPersonaResolver` + `PersonaDecision`.
- `src/LocalDictation.Application/Processing/PersonaResolver.cs` — pure resolution logic.
- `src/LocalDictation.Infrastructure/Persistence/JsonPersonaStore.cs` — mirrors `JsonSettingsStore`.
- `src/LocalDictation.Desktop/Views/PersonaPickerWindow.xaml` (+`.xaml.cs`) — WPF picker palette.
- `src/LocalDictation.Desktop/ViewModels/PersonaRowViewModel.cs` — one persona list row (WPF).
- `src/LocalDictation.Desktop.Avalonia/Views/PersonaPickerWindow.axaml` (+`.axaml.cs`) — Avalonia picker.
- `src/LocalDictation.Desktop.Avalonia/ViewModels/PersonaRowViewModel.cs` — persona row (Avalonia).
- Tests under `tests/LocalDictation.UnitTests/Personas/`.

**Modify**
- `src/LocalDictation.Domain/Enums.cs` — add `PersonaKind` (if not in `Persona.cs`).
- `src/LocalDictation.Application/Abstractions/ITextProcessor.cs` — add `systemPromptOverride` param.
- `src/LocalDictation.Application/Configuration/AppSettings.cs` — add `LlmContextTokens`, `EnhancementTimeoutSeconds`.
- `src/LocalDictation.Application/Pipeline/DictationPipeline.cs` — inject resolver + persona settings; `RunAsync` gains `personaOverride`; use `PersonaDecision`; harden raw fallback; oversized hint.
- `src/LocalDictation.Infrastructure/AppPaths.cs` — add `PersonasFile`.
- `src/LocalDictation.Infrastructure/Ai/OllamaTextProcessor.cs` — `systemPromptOverride`, `num_ctx`, timeout, timeout-vs-cancel fix.
- `src/LocalDictation.Infrastructure/Ai/NoOpTextProcessor.cs` — signature update.
- `src/LocalDictation.Infrastructure/DependencyInjection/InfrastructureModule.cs` — register `IPersonaStore`, `IPersonaResolver`, `PersonaSettings`.
- `src/LocalDictation.Application/Abstractions/WindowsAbstractions.cs` — `HotkeyPressedEventArgs.Action`; `IHotkeyService` picker registration.
- `src/LocalDictation.Infrastructure.Windows/Windows/HotkeyService.cs` — second hotkey id.
- `src/LocalDictation.Infrastructure.Mac/Input/CarbonHotkeyService.cs` — second hotkey (mirror).
- `src/LocalDictation.Desktop/Services/DictationController.cs` — register picker hotkey, show picker, run with override.
- `src/LocalDictation.Desktop.Avalonia/Services/MacDictationController.cs` — mirror.
- `src/LocalDictation.Desktop/App.xaml.cs` + `src/LocalDictation.Desktop.Avalonia/App.axaml.cs` — load `PersonaSettings` at boot; register singleton; wire picker window; register `PersonaPickerWindow`.
- `src/LocalDictation.Desktop/Views/ControlPanelWindow.xaml` (+`.xaml.cs`) + `src/LocalDictation.Desktop/ViewModels/ControlPanelViewModel.cs` — Personas section (WPF).
- `src/LocalDictation.Desktop.Avalonia/Views/ControlPanelWindow.axaml` + `.../ViewModels/ControlPanelViewModel.cs` — Personas section (Avalonia).
- `docs/adr/` — new ADR; `docs/index.html` + `README.md` — document personas + the picker hotkey.

---

## Task 1: Persona entity + PersonaKind enum

**Files:**
- Create: `src/LocalDictation.Domain/Persona.cs`
- Test: `tests/LocalDictation.UnitTests/Personas/PersonaTests.cs`

**Interfaces:**
- Produces: `LocalDictation.Domain.Persona` (mutable class), `LocalDictation.Domain.PersonaKind { System, BuiltIn, User }`, and `Persona.NormalizeProcessName(string) : string`.

- [ ] **Step 1: Write the failing test**

```csharp
using LocalDictation.Domain;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class PersonaTests
{
    [Theory]
    [InlineData("Notion.exe", "notion")]
    [InlineData("Google Chrome", "google chrome")]
    [InlineData("  MS-Teams.EXE ", "ms-teams")]
    public void NormalizeProcessName_lowercases_and_strips_exe(string input, string expected)
        => Assert.Equal(expected, Persona.NormalizeProcessName(input));

    [Fact]
    public void Persona_defaults_are_enabled_user_kind()
    {
        var p = new Persona();
        Assert.True(p.Enabled);
        Assert.Equal(PersonaKind.User, p.Kind);
        Assert.NotNull(p.MatchProcessNames);
        Assert.Empty(p.MatchProcessNames);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~PersonaTests --nologo`
Expected: FAIL — `Persona` / `PersonaKind` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace LocalDictation.Domain;

/// <summary>Provenance of a persona, which governs edit/reset/delete affordances in the UI.</summary>
public enum PersonaKind
{
    /// <summary>Seeded fallback/legacy prompt (General cleanup, Professional rewrite, …). Editable + resettable, not deletable.</summary>
    System,
    /// <summary>Seeded app persona (Notion, Email, …). Editable + resettable, not deletable.</summary>
    BuiltIn,
    /// <summary>User-created. Editable + deletable.</summary>
    User
}

/// <summary>
/// A named, reusable LLM system-prompt applied to a dictation, chosen automatically from the
/// focused app or manually from the picker.
/// </summary>
/// <remarks>
/// Mutable (like <c>AppSettings</c>) so the settings view model can edit it in place and persist.
/// Persona keys are executable names — the one identifier available on both Windows and macOS,
/// even for Electron apps. <see cref="MatchProcessNames"/> is empty for picker-only personas.
/// </remarks>
public sealed class Persona
{
    /// <summary>Stable slug, e.g. "notion", "coding-agent". Used as the identity for default/reset lookups.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name shown in the list and picker.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional icon hint; null falls back to the app mark in the UI.</summary>
    public string? Glyph { get; set; }

    /// <summary>Normalized exe names that auto-trigger this persona. Empty = picker-only / fallback.</summary>
    public List<string> MatchProcessNames { get; set; } = new();

    /// <summary>The instruction sent to the LLM as its system message.</summary>
    public string SystemPrompt { get; set; } = "";

    /// <summary>Whether the persona participates in auto-resolution and appears in the picker.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Provenance; governs whether the UI offers Reset (System/BuiltIn) or Delete (User).</summary>
    public PersonaKind Kind { get; set; } = PersonaKind.User;

    /// <summary>Lowercases and strips a trailing ".exe" so Windows and macOS process names compare equal.</summary>
    /// <example><c>Persona.NormalizeProcessName("Notion.exe") == "notion"</c></example>
    public static string NormalizeProcessName(string processName)
    {
        var s = (processName ?? "").Trim().ToLowerInvariant();
        return s.EndsWith(".exe", StringComparison.Ordinal) ? s[..^4] : s;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~PersonaTests --nologo`
Expected: PASS (4 cases).

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Domain/Persona.cs tests/LocalDictation.UnitTests/Personas/PersonaTests.cs
git commit -m "$(printf 'feat(personas): add Persona entity and PersonaKind\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 2: PersonaSettings + PersonaSeeds (factory defaults)

**Files:**
- Create: `src/LocalDictation.Application/Configuration/PersonaSettings.cs`
- Create: `src/LocalDictation.Application/Processing/PersonaSeeds.cs`
- Test: `tests/LocalDictation.UnitTests/Personas/PersonaSeedsTests.cs`

**Interfaces:**
- Consumes: `Persona`, `PersonaKind` (Task 1), `ProcessingMode` (Domain).
- Produces:
  - `LocalDictation.Application.Configuration.PersonaSettings` with `int SchemaVersion`, `bool AutoApply`, `string? DefaultPersonaId`, `string PickerHotkey`, `List<Persona> Personas`, and `Persona? FindById(string? id)`.
  - `LocalDictation.Application.Processing.PersonaSeeds` with `static PersonaSettings CreateDefaults()`, `static List<Persona> DefaultPersonas()`, `static string? DefaultPromptFor(string id)`, `static string? PersonaIdForMode(ProcessingMode mode)`.
  - Seed persona ids: `general`, `professional`, `summarize`, `markdown` (System); `notion`, `email`, `teams`, `coding-agent` (BuiltIn).

- [ ] **Step 1: Write the failing test**

```csharp
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using LocalDictation.Domain;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class PersonaSeedsTests
{
    [Fact]
    public void Defaults_seed_system_and_builtin_personas()
    {
        var s = PersonaSeeds.CreateDefaults();
        Assert.True(s.AutoApply);
        Assert.Equal("general", s.DefaultPersonaId);
        Assert.Equal("Ctrl+Alt+Space", s.PickerHotkey);

        Assert.Contains(s.Personas, p => p.Id == "general" && p.Kind == PersonaKind.System && p.MatchProcessNames.Count == 0);
        Assert.Contains(s.Personas, p => p.Id == "notion" && p.Kind == PersonaKind.BuiltIn && p.MatchProcessNames.Contains("notion"));
        Assert.Contains(s.Personas, p => p.Id == "coding-agent" && p.Kind == PersonaKind.BuiltIn && p.MatchProcessNames.Count == 0);
        Assert.All(s.Personas, p => Assert.False(string.IsNullOrWhiteSpace(p.SystemPrompt)));
    }

    [Fact]
    public void FindById_matches_case_insensitively_and_handles_null()
    {
        var s = PersonaSeeds.CreateDefaults();
        Assert.Equal("general", s.FindById("General")!.Id);
        Assert.Null(s.FindById(null));
        Assert.Null(s.FindById("nope"));
    }

    [Theory]
    [InlineData(ProcessingMode.GrammarCorrection, "general")]
    [InlineData(ProcessingMode.ProfessionalRewrite, "professional")]
    [InlineData(ProcessingMode.Summarize, "summarize")]
    [InlineData(ProcessingMode.MarkdownFormat, "markdown")]
    [InlineData(ProcessingMode.Translate, null)]
    [InlineData(ProcessingMode.Custom, null)]
    public void PersonaIdForMode_maps_legacy_modes(ProcessingMode mode, string? expected)
        => Assert.Equal(expected, PersonaSeeds.PersonaIdForMode(mode));

    [Fact]
    public void DefaultPromptFor_returns_seed_prompt_for_reset()
        => Assert.Equal(PersonaSeeds.DefaultPersonas().First(p => p.Id == "general").SystemPrompt,
                        PersonaSeeds.DefaultPromptFor("general"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~PersonaSeedsTests --nologo`
Expected: FAIL — `PersonaSettings` / `PersonaSeeds` do not exist.

- [ ] **Step 3: Write minimal implementation**

`PersonaSettings.cs`:

```csharp
using LocalDictation.Domain;

namespace LocalDictation.Application.Configuration;

/// <summary>
/// Persisted persona configuration (<c>personas.json</c>), a sibling of <see cref="AppSettings"/>.
/// Kept separate because the file doubles as the import/export format.
/// </summary>
public sealed class PersonaSettings
{
    /// <summary>Schema version for forward-compatible migration.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Master switch for auto-detection. When off, personas apply only via the picker.</summary>
    public bool AutoApply { get; set; } = true;

    /// <summary>Id of the fallback persona used when no app matches (and it is enabled).</summary>
    public string? DefaultPersonaId { get; set; } = "general";

    /// <summary>Second global hotkey that opens the persona picker.</summary>
    public string PickerHotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>All personas — System, BuiltIn and User.</summary>
    public List<Persona> Personas { get; set; } = new();

    /// <summary>Finds a persona by id (case-insensitive); null id/miss returns null.</summary>
    public Persona? FindById(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : Personas.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
```

`PersonaSeeds.cs`:

```csharp
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Application.Processing;

/// <summary>
/// Factory defaults for personas. The System personas carry the former hardcoded
/// <see cref="PromptTemplates"/> prompts, now editable; this class is also the "Reset to default"
/// source and the legacy-mode → persona mapping used when auto-apply is off.
/// </summary>
public static class PersonaSeeds
{
    /// <summary>A fresh <see cref="PersonaSettings"/> with all seed personas and default options.</summary>
    public static PersonaSettings CreateDefaults() => new() { Personas = DefaultPersonas() };

    /// <summary>The seed persona list (new instances each call, safe to mutate).</summary>
    public static List<Persona> DefaultPersonas() => new()
    {
        new Persona { Id = "general", Name = "General cleanup", Kind = PersonaKind.System,
            SystemPrompt = "You are a dictation cleanup engine. Fix grammar, spelling, punctuation and " +
                "capitalization in the user's transcribed speech WITHOUT changing meaning, wording style, " +
                "or adding content. Remove filler words and false starts. Output ONLY the corrected text." },
        new Persona { Id = "professional", Name = "Professional rewrite", Kind = PersonaKind.System,
            SystemPrompt = "Rewrite the user's transcribed speech in clear, concise, professional English. " +
                "Preserve all facts and intent. Output ONLY the rewritten text." },
        new Persona { Id = "summarize", Name = "Summarize", Kind = PersonaKind.System,
            SystemPrompt = "Summarize the user's transcribed speech into a short, faithful summary. Output ONLY the summary." },
        new Persona { Id = "markdown", Name = "Markdown", Kind = PersonaKind.System,
            SystemPrompt = "Format the user's transcribed speech as clean Markdown (headings, lists, code blocks " +
                "where appropriate) without changing the wording. Output ONLY the Markdown." },

        new Persona { Id = "notion", Name = "Notion", Kind = PersonaKind.BuiltIn,
            MatchProcessNames = new() { "notion" },
            SystemPrompt = "Convert the user's spoken notes into clean, well-structured Markdown for Notion: " +
                "headings, bullet and numbered lists, tables, block quotes and callouts where they fit. " +
                "Keep a documentation style. Preserve meaning; do not invent content. Output ONLY the Markdown." },
        new Persona { Id = "email", Name = "Email", Kind = PersonaKind.BuiltIn,
            MatchProcessNames = new() { "outlook" },
            SystemPrompt = "Turn the user's dictation into a professional email: a suitable greeting, a well-" +
                "structured body, and a closing. Improve grammar and tone while preserving intent. Output ONLY the email." },
        new Persona { Id = "teams", Name = "Teams", Kind = PersonaKind.BuiltIn,
            MatchProcessNames = new() { "ms-teams", "teams" },
            SystemPrompt = "Rewrite the user's dictation as a short, conversational chat message: remove filler " +
                "words, keep the meaning, friendly-professional tone. Output ONLY the message." },
        new Persona { Id = "coding-agent", Name = "Coding Agent", Kind = PersonaKind.BuiltIn,
            SystemPrompt = "Rewrite the user's dictation into a precise, well-structured implementation prompt for " +
                "a coding agent. Organize loose speech into clear requirements. Preserve every technical detail " +
                "verbatim — file names, APIs, identifiers, versions. Clarify intent without inventing scope. " +
                "Prefer numbered steps and short constraint bullets. Output ONLY the prompt, no preamble." }
    };

    /// <summary>The seed system-prompt for a persona id, for "Reset to default"; null if unknown.</summary>
    public static string? DefaultPromptFor(string id) =>
        DefaultPersonas().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))?.SystemPrompt;

    /// <summary>Maps a legacy cleanup mode to the System persona that now owns its (editable) prompt.</summary>
    public static string? PersonaIdForMode(ProcessingMode mode) => mode switch
    {
        ProcessingMode.GrammarCorrection => "general",
        ProcessingMode.ProfessionalRewrite => "professional",
        ProcessingMode.Summarize => "summarize",
        ProcessingMode.MarkdownFormat => "markdown",
        _ => null // Translate/Custom/None keep legacy PromptTemplates handling
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~PersonaSeedsTests --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Application/Configuration/PersonaSettings.cs src/LocalDictation.Application/Processing/PersonaSeeds.cs tests/LocalDictation.UnitTests/Personas/PersonaSeedsTests.cs
git commit -m "$(printf 'feat(personas): add PersonaSettings and PersonaSeeds defaults\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 3: JsonPersonaStore + AppPaths.PersonasFile

**Files:**
- Create: `src/LocalDictation.Application/Abstractions/IPersonaStore.cs`
- Create: `src/LocalDictation.Infrastructure/Persistence/JsonPersonaStore.cs`
- Modify: `src/LocalDictation.Infrastructure/AppPaths.cs` (add `PersonasFile` after line 37)
- Test: `tests/LocalDictation.UnitTests/Personas/JsonPersonaStoreTests.cs`

**Interfaces:**
- Consumes: `PersonaSettings`, `PersonaSeeds` (Task 2).
- Produces: `IPersonaStore { Task<PersonaSettings> LoadAsync(ct); Task SaveAsync(PersonaSettings, ct); }`; `JsonPersonaStore(string path, ILogger<JsonPersonaStore>)`; `AppPaths.PersonasFile`.

- [ ] **Step 1: Write the failing test**

```csharp
using LocalDictation.Application.Configuration;
using LocalDictation.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class JsonPersonaStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"personas-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Load_first_run_writes_and_returns_seed_defaults()
    {
        var path = TempFile();
        try
        {
            var store = new JsonPersonaStore(path, NullLogger<JsonPersonaStore>.Instance);
            var loaded = await store.LoadAsync();
            Assert.True(File.Exists(path));
            Assert.Contains(loaded.Personas, p => p.Id == "general");
            Assert.Equal("general", loaded.DefaultPersonaId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Save_then_Load_round_trips_edits()
    {
        var path = TempFile();
        try
        {
            var store = new JsonPersonaStore(path, NullLogger<JsonPersonaStore>.Instance);
            var s = PersonaSeeds.CreateDefaults();
            s.FindById("general")!.SystemPrompt = "EDITED PROMPT";
            s.AutoApply = false;
            await store.SaveAsync(s);

            var reloaded = await store.LoadAsync();
            Assert.Equal("EDITED PROMPT", reloaded.FindById("general")!.SystemPrompt);
            Assert.False(reloaded.AutoApply);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_corrupt_file_reseeds_defaults()
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, "{ not json");
            var store = new JsonPersonaStore(path, NullLogger<JsonPersonaStore>.Instance);
            var loaded = await store.LoadAsync();
            Assert.Contains(loaded.Personas, p => p.Id == "general");
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~JsonPersonaStoreTests --nologo`
Expected: FAIL — `IPersonaStore` / `JsonPersonaStore` do not exist.

- [ ] **Step 3a: Add the port** — `IPersonaStore.cs`

```csharp
using LocalDictation.Application.Configuration;

namespace LocalDictation.Application.Abstractions;

/// <summary>Loads and saves persona configuration (<c>personas.json</c>).</summary>
public interface IPersonaStore
{
    /// <summary>Loads personas, seeding defaults on first run or an unreadable file.</summary>
    Task<PersonaSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Atomically persists persona configuration.</summary>
    Task SaveAsync(PersonaSettings settings, CancellationToken ct = default);
}
```

- [ ] **Step 3b: Add `PersonasFile` to `AppPaths`**

In `src/LocalDictation.Infrastructure/AppPaths.cs`, add the property beside the others (after `SettingsFile`, ~line 24):

```csharp
    /// <summary>personas.json path.</summary>
    public string PersonasFile { get; }
```

and in the constructor after the `SettingsFile` assignment (~line 37):

```csharp
        PersonasFile = Path.Combine(Root, "personas.json");
```

- [ ] **Step 3c: Implement `JsonPersonaStore.cs`** (mirrors `JsonSettingsStore`)

```csharp
using System.Text.Json;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Persistence;

/// <summary>
/// Loads/saves <see cref="PersonaSettings"/> as JSON beside <c>settings.json</c>. Writes atomically
/// (temp + rename); seeds factory defaults on first run or an unreadable file. The same file format
/// is used for Import/Export.
/// </summary>
public sealed class JsonPersonaStore : IPersonaStore
{
    private const int CurrentSchema = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly ILogger<JsonPersonaStore> _log;

    /// <summary>Creates the store rooted at <paramref name="personasPath"/>.</summary>
    public JsonPersonaStore(string personasPath, ILogger<JsonPersonaStore> log)
    {
        _path = personasPath;
        _log = log;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    /// <inheritdoc />
    public async Task<PersonaSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                var defaults = PersonaSeeds.CreateDefaults();
                await SaveAsync(defaults, ct).ConfigureAwait(false);
                return defaults;
            }

            var json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<PersonaSettings>(json, Options);
            if (settings is null || settings.Personas.Count == 0) return PersonaSeeds.CreateDefaults();
            return Migrate(settings);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load personas; using defaults.");
            return PersonaSeeds.CreateDefaults();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(PersonaSettings settings, CancellationToken ct = default)
    {
        settings.SchemaVersion = CurrentSchema;
        var json = JsonSerializer.Serialize(settings, Options);
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        File.Move(tmp, _path, overwrite: true);
    }

    private PersonaSettings Migrate(PersonaSettings settings)
    {
        if (settings.SchemaVersion < CurrentSchema)
        {
            _log.LogInformation("Migrating personas from schema {From} to {To}", settings.SchemaVersion, CurrentSchema);
            settings.SchemaVersion = CurrentSchema;
        }
        return settings;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~JsonPersonaStoreTests --nologo`
Expected: PASS (3 cases).

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Application/Abstractions/IPersonaStore.cs src/LocalDictation.Infrastructure/Persistence/JsonPersonaStore.cs src/LocalDictation.Infrastructure/AppPaths.cs tests/LocalDictation.UnitTests/Personas/JsonPersonaStoreTests.cs
git commit -m "$(printf 'feat(personas): add JsonPersonaStore and AppPaths.PersonasFile\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 4: PersonaResolver + PersonaDecision

**Files:**
- Create: `src/LocalDictation.Application/Abstractions/IPersonaResolver.cs`
- Create: `src/LocalDictation.Application/Processing/PersonaResolver.cs`
- Test: `tests/LocalDictation.UnitTests/Personas/PersonaResolverTests.cs`

**Interfaces:**
- Consumes: `Persona`, `TargetControl`, `ProcessingMode` (Domain); `AppSettings`, `PersonaSettings`, `PersonaSeeds` (Application).
- Produces:
  - `readonly record struct PersonaDecision(bool Enhance, ProcessingMode Mode, string? SystemPrompt, string? PersonaName)`.
  - `IPersonaResolver { PersonaDecision Decide(TargetControl target, AppSettings settings, PersonaSettings personas, Persona? explicitOverride); }`.
  - `PersonaResolver : IPersonaResolver` (stateless).

Decision rules (implement exactly):
1. `aiOn = settings.AiEnabled || explicitOverride is not null` (picker forces AI on).
2. If `!aiOn` → `(false, None, null, null)`.
3. Choose persona: `explicitOverride` if set; else if `personas.AutoApply` → first **enabled** persona whose `MatchProcessNames` contains `Persona.NormalizeProcessName(target.ProcessName)`, else the enabled default (`personas.FindById(personas.DefaultPersonaId)`); else (AutoApply off) → the enabled System persona for `PersonaSeeds.PersonaIdForMode(settings.DefaultMode)`.
4. If a persona is chosen **and enabled** → `(true, Custom, persona.SystemPrompt, persona.Name)`.
5. Else → `(true, settings.DefaultMode, null, null)` (legacy `PromptTemplates`, e.g. Translate).

- [ ] **Step 1: Write the failing test**

```csharp
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using LocalDictation.Domain;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class PersonaResolverTests
{
    private static TargetControl Target(string proc) => new() { ProcessName = proc };
    private readonly PersonaResolver _r = new();

    [Fact]
    public void Ai_off_and_no_override_does_not_enhance()
    {
        var d = _r.Decide(Target("notion"), new AppSettings { AiEnabled = false },
                          PersonaSeeds.CreateDefaults(), null);
        Assert.False(d.Enhance);
        Assert.Equal(ProcessingMode.None, d.Mode);
    }

    [Fact]
    public void Auto_apply_matches_process_name_to_persona()
    {
        var d = _r.Decide(Target("Notion.exe"), new AppSettings { AiEnabled = true },
                          PersonaSeeds.CreateDefaults(), null);
        Assert.True(d.Enhance);
        Assert.Equal(ProcessingMode.Custom, d.Mode);
        Assert.Equal("Notion", d.PersonaName);
        Assert.Contains("Notion", d.SystemPrompt);
    }

    [Fact]
    public void No_match_falls_back_to_default_persona()
    {
        var d = _r.Decide(Target("mspaint"), new AppSettings { AiEnabled = true },
                          PersonaSeeds.CreateDefaults(), null);
        Assert.Equal("General cleanup", d.PersonaName);
    }

    [Fact]
    public void Disabled_matching_persona_is_skipped()
    {
        var personas = PersonaSeeds.CreateDefaults();
        personas.FindById("notion")!.Enabled = false;
        var d = _r.Decide(Target("notion"), new AppSettings { AiEnabled = true }, personas, null);
        Assert.Equal("General cleanup", d.PersonaName); // falls through to default
    }

    [Fact]
    public void Explicit_override_wins_and_forces_ai_on()
    {
        var personas = PersonaSeeds.CreateDefaults();
        var coding = personas.FindById("coding-agent")!;
        var d = _r.Decide(Target("WindowsTerminal"), new AppSettings { AiEnabled = false }, personas, coding);
        Assert.True(d.Enhance);
        Assert.Equal("Coding Agent", d.PersonaName);
    }

    [Fact]
    public void Auto_apply_off_maps_default_mode_to_system_persona_prompt()
    {
        var personas = PersonaSeeds.CreateDefaults();
        personas.AutoApply = false;
        personas.FindById("professional")!.SystemPrompt = "EDITED PRO";
        var d = _r.Decide(Target("notion"),
                          new AppSettings { AiEnabled = true, DefaultMode = ProcessingMode.ProfessionalRewrite },
                          personas, null);
        Assert.Equal("EDITED PRO", d.SystemPrompt); // edited System persona wins over hardcoded template
    }

    [Fact]
    public void Auto_apply_off_translate_uses_legacy_mode()
    {
        var personas = PersonaSeeds.CreateDefaults();
        personas.AutoApply = false;
        var d = _r.Decide(Target("notion"),
                          new AppSettings { AiEnabled = true, DefaultMode = ProcessingMode.Translate },
                          personas, null);
        Assert.True(d.Enhance);
        Assert.Equal(ProcessingMode.Translate, d.Mode);
        Assert.Null(d.SystemPrompt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~PersonaResolverTests --nologo`
Expected: FAIL — `IPersonaResolver`/`PersonaResolver` do not exist.

- [ ] **Step 3a: Add the port** — `IPersonaResolver.cs`

```csharp
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Application.Abstractions;

/// <summary>The prompt decision for one dictation.</summary>
/// <param name="Enhance">Whether AI enhancement runs at all.</param>
/// <param name="Mode">Processing mode for the legacy path; <c>Custom</c> when a persona prompt is used.</param>
/// <param name="SystemPrompt">Persona system-prompt override, or null to use <see cref="Processing.PromptTemplates"/>.</param>
/// <param name="PersonaName">Resolved persona name for the overlay/history, or null.</param>
public readonly record struct PersonaDecision(bool Enhance, ProcessingMode Mode, string? SystemPrompt, string? PersonaName);

/// <summary>Resolves the focused app (or an explicit picker choice) to a prompt decision.</summary>
public interface IPersonaResolver
{
    /// <summary>Decides whether/how to enhance a dictation. See the plan for the exact rules.</summary>
    PersonaDecision Decide(TargetControl target, AppSettings settings, PersonaSettings personas, Persona? explicitOverride);
}
```

- [ ] **Step 3b: Implement `PersonaResolver.cs`**

```csharp
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Application.Processing;

/// <summary>
/// Pure resolution of focused app / picker choice → <see cref="PersonaDecision"/>. Personas never
/// gate whether AI runs; they only select which prompt is used. The ladder is: explicit override →
/// matched persona → default persona → (auto-apply off) the legacy mode's System persona → raw mode.
/// </summary>
public sealed class PersonaResolver : IPersonaResolver
{
    /// <inheritdoc />
    public PersonaDecision Decide(TargetControl target, AppSettings settings, PersonaSettings personas, Persona? explicitOverride)
    {
        var aiOn = settings.AiEnabled || explicitOverride is not null;
        if (!aiOn) return new PersonaDecision(false, ProcessingMode.None, null, null);

        var persona = explicitOverride
            ?? (personas.AutoApply
                ? MatchByProcess(target, personas) ?? EnabledDefault(personas)
                : ModeSystemPersona(settings.DefaultMode, personas));

        if (persona is { Enabled: true })
            return new PersonaDecision(true, ProcessingMode.Custom, persona.SystemPrompt, persona.Name);

        return new PersonaDecision(true, settings.DefaultMode, null, null);
    }

    private static Persona? MatchByProcess(TargetControl target, PersonaSettings personas)
    {
        var key = Persona.NormalizeProcessName(target.ProcessName);
        if (string.IsNullOrEmpty(key)) return null;
        return personas.Personas.FirstOrDefault(p => p.Enabled && p.MatchProcessNames.Contains(key));
    }

    private static Persona? EnabledDefault(PersonaSettings personas)
    {
        var d = personas.FindById(personas.DefaultPersonaId);
        return d is { Enabled: true } ? d : null;
    }

    private static Persona? ModeSystemPersona(ProcessingMode mode, PersonaSettings personas)
    {
        var id = PersonaSeeds.PersonaIdForMode(mode);
        var p = personas.FindById(id);
        return p is { Enabled: true } ? p : null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~PersonaResolverTests --nologo`
Expected: PASS (7 cases).

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Application/Abstractions/IPersonaResolver.cs src/LocalDictation.Application/Processing/PersonaResolver.cs tests/LocalDictation.UnitTests/Personas/PersonaResolverTests.cs
git commit -m "$(printf 'feat(personas): add PersonaResolver decision logic\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 5: Text processor — systemPromptOverride, num_ctx, generous timeout, timeout-vs-cancel fix

**Files:**
- Modify: `src/LocalDictation.Application/Abstractions/ITextProcessor.cs`
- Modify: `src/LocalDictation.Infrastructure/Ai/OllamaTextProcessor.cs`
- Modify: `src/LocalDictation.Infrastructure/Ai/NoOpTextProcessor.cs`
- Modify: `src/LocalDictation.Application/Configuration/AppSettings.cs` (add two fields)
- Test: `tests/LocalDictation.UnitTests/Personas/OllamaTextProcessorTests.cs`

**Interfaces:**
- Produces: `ITextProcessor.ProcessAsync(string text, ProcessingMode mode, string targetLanguage = "en", string? customPrompt = null, string? systemPromptOverride = null, CancellationToken ct = default)`; `AppSettings.LlmContextTokens` (int, default 8192), `AppSettings.EnhancementTimeoutSeconds` (int, default 300).
- Note: adding `systemPromptOverride` before `ct` shifts positional args — the pipeline call site is fixed in Task 6.

- [ ] **Step 1: Write the failing test** (stub `HttpMessageHandler` captures the request body)

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class OllamaTextProcessorTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            var json = "{\"message\":{\"role\":\"assistant\",\"content\":\"clean text\"}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Override_is_used_as_system_message_and_num_ctx_is_sent()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var settings = new AppSettings { LlmContextTokens = 8192 };
        var proc = new OllamaTextProcessor(http, settings, NullLogger<OllamaTextProcessor>.Instance);

        var result = await proc.ProcessAsync("raw words", ProcessingMode.Custom,
            systemPromptOverride: "PERSONA SYSTEM PROMPT");

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(handler.Body!);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal("PERSONA SYSTEM PROMPT", messages[0].GetProperty("content").GetString());
        Assert.Equal("raw words", messages[1].GetProperty("content").GetString());
        Assert.Equal(8192, doc.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~OllamaTextProcessorTests --nologo`
Expected: FAIL — `ProcessAsync` has no `systemPromptOverride` parameter / no `num_ctx`.

- [ ] **Step 3a: Update `ITextProcessor.ProcessAsync`** (add the param + doc line)

```csharp
    /// <param name="systemPromptOverride">When set, used verbatim as the system message (persona), with the raw text as the user turn; bypasses <see cref="ProcessingMode"/> prompt selection.</param>
    Task<Result<string>> ProcessAsync(
        string text,
        ProcessingMode mode,
        string targetLanguage = "en",
        string? customPrompt = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default);
```

- [ ] **Step 3b: Update `NoOpTextProcessor.ProcessAsync` signature**

```csharp
    public Task<Result<string>> ProcessAsync(
        string text, ProcessingMode mode, string targetLanguage = "en",
        string? customPrompt = null, string? systemPromptOverride = null, CancellationToken ct = default)
        => Task.FromResult(Result<string>.Ok(text));
```

- [ ] **Step 3c: Update `AppSettings`** — add under the `// ---- AI ----` region (after `KeepModelResident`):

```csharp
    /// <summary>Context window (num_ctx) requested from Ollama for enhancement. Larger = fewer silent
    /// truncations on long dictations, at higher RAM. 0 leaves the server default.</summary>
    public int LlmContextTokens { get; set; } = 8192;

    /// <summary>HTTP timeout (seconds) for a single enhancement call. Generous so long CPU-only
    /// dictations don't hit the 100 s HttpClient default and lose enhancement.</summary>
    public int EnhancementTimeoutSeconds { get; set; } = 300;
```

- [ ] **Step 3d: Update `OllamaTextProcessor`**

Constructor — set the timeout (after the `BaseAddress` block):

```csharp
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(settings.OllamaUrl);
        if (settings.EnhancementTimeoutSeconds > 0)
            _http.Timeout = TimeSpan.FromSeconds(settings.EnhancementTimeoutSeconds);
```

`ProcessAsync` signature + body — replace the method signature and the request/message construction:

```csharp
    public async Task<Result<string>> ProcessAsync(
        string text, ProcessingMode mode, string targetLanguage = "en",
        string? customPrompt = null, string? systemPromptOverride = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Result<string>.Ok(text);
        if (mode == ProcessingMode.None && systemPromptOverride is null) return Result<string>.Ok(text);

        try
        {
            var system = systemPromptOverride ?? PromptTemplates.SystemPrompt(mode, targetLanguage);
            var user = systemPromptOverride is not null ? text : PromptTemplates.UserPrompt(text, mode, customPrompt);
            var request = new ChatRequest
            {
                Model = _settings.LlmModel,
                Stream = false,
                KeepAlive = _settings.KeepModelResident ? "15m" : "0s",
                Options = new ChatOptions
                {
                    Temperature = 0.2,
                    NumCtx = _settings.LlmContextTokens > 0 ? _settings.LlmContextTokens : null
                },
                Messages = new[]
                {
                    new ChatMessage("system", system),
                    new ChatMessage("user", user)
                }
            };

            using var resp = await _http.PostAsJsonAsync("/api/chat", request, ct);
            if (!resp.IsSuccessStatusCode)
                return Result<string>.Fail($"Ollama returned {(int)resp.StatusCode}.");

            var body = await resp.Content.ReadFromJsonAsync<ChatResponse>(ct);
            var content = body?.Message?.Content?.Trim();
            return string.IsNullOrWhiteSpace(content)
                ? Result<string>.Fail("Empty LLM response.")
                : Result<string>.Ok(Sanitize(content!));
        }
        // Only a genuine user cancellation (ESC) propagates; an HTTP-level timeout (token not the
        // user's) is a failure we degrade from, so the pipeline falls back to the raw transcript.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ollama processing failed.");
            return Result<string>.Fail(ex.Message);
        }
    }
```

Add `NumCtx` to the private `ChatOptions` class:

```csharp
    private sealed class ChatOptions
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("num_ctx")] public int? NumCtx { get; set; }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~OllamaTextProcessorTests --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Application/Abstractions/ITextProcessor.cs src/LocalDictation.Infrastructure/Ai/OllamaTextProcessor.cs src/LocalDictation.Infrastructure/Ai/NoOpTextProcessor.cs src/LocalDictation.Application/Configuration/AppSettings.cs tests/LocalDictation.UnitTests/Personas/OllamaTextProcessorTests.cs
git commit -m "$(printf 'feat(personas): text processor accepts persona prompt, num_ctx and generous timeout\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 6: Pipeline integration (decision, override, hardened fallback, oversized hint)

**Files:**
- Modify: `src/LocalDictation.Application/Pipeline/DictationPipeline.cs`
- Test: `tests/LocalDictation.UnitTests/Personas/DictationPipelinePersonaTests.cs`

**Interfaces:**
- Consumes: `IPersonaResolver`/`PersonaDecision` (Task 4), `PersonaSettings` (Task 2), updated `ProcessAsync` (Task 5), `AppSettings.LlmContextTokens`.
- Produces: `DictationPipeline` ctor adds `IPersonaResolver resolver, PersonaSettings personas`; `RunAsync(..., CancellationToken ct, Persona? personaOverride = null)`; `DictationOutcome` gains trailing `bool Oversized = false`.

> Note: this changes the `DictationPipeline` ctor and `RunAsync`/`DictationOutcome`. Any existing test or caller that constructs the pipeline or asserts `DictationOutcome` must be updated (search `new DictationPipeline(` and `new DictationOutcome(`). The controller call site is updated in Task 7/9.

- [ ] **Step 1: Write the failing test**

```csharp
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Pipeline;
using LocalDictation.Application.Processing;
using LocalDictation.Domain;
using LocalDictation.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class DictationPipelinePersonaTests
{
    private static AudioClip Clip() => new(new float[16000], 16000); // 1s non-empty; adjust to real ctor

    private static (DictationPipeline pipe, Mock<ITextProcessor> proc) Build(AppSettings settings, PersonaSettings personas)
    {
        var speech = new Mock<ISpeechEngine>();
        speech.SetupGet(s => s.Status).Returns(new SpeechEngineStatus(SpeechReadiness.Ready, ""));
        speech.Setup(s => s.TranscribeAsync(It.IsAny<AudioClip>(), It.IsAny<TranscribeOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result<Transcript>.Ok(new Transcript { RawText = "hello world" }));
        var proc = new Mock<ITextProcessor>();
        proc.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        proc.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingMode>(), It.IsAny<string>(),
                        It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Ok("ENHANCED"));
        var router = new Mock<IOutputRouter>();
        router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<TargetControl>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new OutputResult(true, "clipboard"));
        var history = new Mock<IHistoryRepository>();
        var pipe = new DictationPipeline(speech.Object, proc.Object, router.Object, history.Object,
            new PersonaResolver(), personas, NullLogger<DictationPipeline>.Instance);
        return (pipe, proc);
    }

    [Fact]
    public async Task Matched_persona_prompt_is_passed_as_override()
    {
        var personas = PersonaSeeds.CreateDefaults();
        var (pipe, proc) = Build(new AppSettings { AiEnabled = true }, personas);
        await pipe.RunAsync(Clip(), new TargetControl { ProcessName = "notion" }, new AppSettings { AiEnabled = true }, CancellationToken.None);
        proc.Verify(p => p.ProcessAsync("hello world", ProcessingMode.Custom, It.IsAny<string>(),
            It.IsAny<string?>(), It.Is<string?>(s => s != null && s.Contains("Notion")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Picker_override_enhances_even_when_ai_disabled()
    {
        var personas = PersonaSeeds.CreateDefaults();
        var (pipe, proc) = Build(new AppSettings { AiEnabled = false }, personas);
        var coding = personas.FindById("coding-agent")!;
        await pipe.RunAsync(Clip(), new TargetControl { ProcessName = "WindowsTerminal" },
            new AppSettings { AiEnabled = false }, CancellationToken.None, personaOverride: coding);
        proc.Verify(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingMode>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

> The exact `AudioClip`, `Transcript`, `SpeechEngineStatus`, `OutputResult`, `TranscribeOptions` constructors may differ — before writing the test, open `src/LocalDictation.Domain/` and `src/LocalDictation.Application/Abstractions/` to confirm the real constructors/property setters and adjust the mock setups. Do not invent members.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LocalDictation.UnitTests/LocalDictation.UnitTests.csproj --filter FullyQualifiedName~DictationPipelinePersonaTests --nologo`
Expected: FAIL — ctor arity / `RunAsync` overload mismatch.

- [ ] **Step 3a: Update the `DictationPipeline` fields + ctor** — add resolver and personas:

```csharp
    private readonly IHistoryRepository _history;
    private readonly IPersonaResolver _resolver;
    private readonly PersonaSettings _personas;
    private readonly ILogger<DictationPipeline> _log;

    public DictationPipeline(
        ISpeechEngine speech,
        ITextProcessor processor,
        IOutputRouter router,
        IHistoryRepository history,
        IPersonaResolver resolver,
        PersonaSettings personas,
        ILogger<DictationPipeline> log)
    {
        _speech = speech;
        _processor = processor;
        _router = router;
        _history = history;
        _resolver = resolver;
        _personas = personas;
        _log = log;
    }
```

- [ ] **Step 3b: Update `RunAsync`** — signature, decision, oversized check, processing call:

Replace the signature and the first block (through `session` creation):

```csharp
    public async Task<DictationOutcome> RunAsync(
        AudioClip clip, TargetControl target, AppSettings settings, CancellationToken ct, Persona? personaOverride = null)
    {
        var decision = _resolver.Decide(target, settings, _personas, personaOverride);
        var mode = decision.Enhance ? decision.Mode : ProcessingMode.None;
        var session = new DictationSession(target, mode);
```

Replace the "Optional AI processing" block (mode gate + call) with:

```csharp
        // --- 2. Optional AI processing (degrades gracefully to raw text) ---
        var oversized = false;
        if (decision.Enhance && mode != ProcessingMode.None)
        {
            // Pre-flight: a transcript far larger than the context budget will be silently truncated
            // by Ollama. Flag it so the caller can warn; enhancement still runs and raw is preserved.
            var budget = settings.LlmContextTokens > 0 ? settings.LlmContextTokens : 4096;
            oversized = transcript.RawText.Length / 4 > budget * 0.75;
            if (oversized) StartupLogSafe($"Transcript ~{transcript.RawText.Length / 4} tokens exceeds safe budget ({budget}); may truncate.");

            session.Transition(SessionState.Processing);
            var processed = await ProcessSafelyAsync(transcript.RawText, mode, settings, decision.SystemPrompt, ct);
            if (ct.IsCancellationRequested) return Cancel(session);
            transcript.ProcessedText = processed;
        }
```

> `StartupLogSafe` is a tiny helper — the Application layer cannot reference the Desktop `StartupLog`, so log via the injected logger instead. Replace the `StartupLogSafe(...)` call above with `_log.LogWarning("{Msg}", ...)`:
> ```csharp
>             if (oversized) _log.LogWarning("Transcript ~{Tokens} tokens exceeds safe budget ({Budget}); may truncate.", transcript.RawText.Length / 4, budget);
> ```
> (Do not add a `StartupLogSafe` method.)

Update the two `return new DictationOutcome(...)` at the end of the success path to pass `oversized` as the trailing arg:

```csharp
        return new DictationOutcome(
            session,
            delivery.Success,
            delivery.Success ? $"Inserted via {delivery.StrategyUsed}." : "Opened floating editor.",
            delivery.Success ? DictationFailure.None : DictationFailure.DeliveredToEditor,
            oversized);
```

- [ ] **Step 3c: Update `ProcessSafelyAsync`** — thread the override and harden the cancel/timeout distinction:

```csharp
    private async Task<string> ProcessSafelyAsync(string raw, ProcessingMode mode, AppSettings settings, string? systemPromptOverride, CancellationToken ct)
    {
        try
        {
            if (!await _processor.IsAvailableAsync(ct)) return raw;
            var result = await _processor.ProcessAsync(raw, mode, settings.TranslationTarget, settings.CustomPrompt, systemPromptOverride, ct);
            return result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value) ? result.Value! : raw;
        }
        // Only a genuine user cancellation propagates; an HTTP timeout degrades to raw text.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AI processing failed; using raw transcript.");
            return raw;
        }
    }
```

- [ ] **Step 3d: Update `DictationOutcome`** — add the trailing field:

```csharp
public readonly record struct DictationOutcome(
    DictationSession Session, bool Delivered, string Message,
    DictationFailure Failure = DictationFailure.None, bool Oversized = false);
```

- [ ] **Step 3e: Add the `using`** at the top of `DictationPipeline.cs`: `using LocalDictation.Application.Processing;` is not needed (resolver is injected), but ensure `LocalDictation.Domain` is imported for `Persona` (it already imports `LocalDictation.Domain`).

- [ ] **Step 4: Run test to verify it passes** (and the full suite, to catch updated ctor/outcome call sites)

Run: `dotnet build LocalDictation.sln -c Debug --nologo` then `dotnet test LocalDictation.sln --nologo`
Expected: build succeeds after fixing any other `new DictationPipeline(`/`new DictationOutcome(` call sites the compiler flags; all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Application/Pipeline/DictationPipeline.cs tests/LocalDictation.UnitTests/Personas/DictationPipelinePersonaTests.cs
git commit -m "$(printf 'feat(personas): resolve and apply persona prompt in the pipeline\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 7: DI wiring + boot load of PersonaSettings

**Files:**
- Modify: `src/LocalDictation.Infrastructure/DependencyInjection/InfrastructureModule.cs`
- Modify: `src/LocalDictation.Desktop/App.xaml.cs`
- Modify: `src/LocalDictation.Desktop.Avalonia/App.axaml.cs`

**Interfaces:**
- Consumes: `IPersonaStore`/`JsonPersonaStore` (Task 3), `IPersonaResolver`/`PersonaResolver` (Task 4), `PersonaSettings` (Task 2).
- Produces: `PersonaSettings` registered as a DI singleton (loaded at boot, like `AppSettings`), `IPersonaStore` and `IPersonaResolver` registered.

- [ ] **Step 1: Register store + resolver in `AddCoreInfrastructure`**

In `InfrastructureModule.cs`, inside the `// ---- Persistence ----` region (after the `ISettingsStore` registration):

```csharp
        services.AddSingleton<IPersonaStore>(sp => new JsonPersonaStore(
            sp.GetRequiredService<AppPaths>().PersonasFile,
            sp.GetRequiredService<ILogger<JsonPersonaStore>>()));
```

and add, near the `// ---- Orchestration ----` region (before `DictationPipeline`):

```csharp
        // ---- Personas ----
        services.AddSingleton<IPersonaResolver, PersonaResolver>();
```

Add the required `using`s at the top: `using LocalDictation.Application.Processing;` (for `PersonaResolver`). `IPersonaResolver`/`IPersonaStore` are in `LocalDictation.Application.Abstractions` (already imported).

> `PersonaSettings` itself is NOT registered here (it needs an async load). It is loaded and registered in `App` boot below, mirroring how `AppSettings` is loaded before the container is built. Confirm by reading `App.xaml.cs`: find where `AppSettings` is loaded via `ISettingsStore.LoadAsync()` / registered as a singleton, and add the persona load right beside it.

- [ ] **Step 2: Load + register `PersonaSettings` in `App.xaml.cs` (WPF)**

Read `src/LocalDictation.Desktop/App.xaml.cs`. Locate where `AppSettings` is obtained (e.g. `var settings = await settingsStore.LoadAsync();`) and registered (`services.AddSingleton(settings);`). Immediately after, add the equivalent persona load. If settings are loaded before the container exists, load personas the same way using a `JsonPersonaStore` built from `AppPaths`, e.g.:

```csharp
        var personaStore = new JsonPersonaStore(appPaths.PersonasFile,
            loggerFactory.CreateLogger<JsonPersonaStore>());
        var personas = await personaStore.LoadAsync();
        services.AddSingleton(personas); // PersonaSettings singleton, injected into DictationPipeline
```

Match the exact idiom already used for `AppSettings` (same logger source, same `AppPaths` instance). Add `using LocalDictation.Application.Configuration;` and `using LocalDictation.Infrastructure.Persistence;` if not present.

- [ ] **Step 3: Mirror in `App.axaml.cs` (Avalonia)**

Apply the identical load+register in `src/LocalDictation.Desktop.Avalonia/App.axaml.cs` at the matching point where `AppSettings` is loaded/registered.

- [ ] **Step 4: Build + run the full suite**

Run: `dotnet build LocalDictation.sln -c Debug --nologo` then `dotnet test LocalDictation.sln --nologo`
Expected: build succeeds; DI resolves `DictationPipeline` (which now needs `IPersonaResolver` + `PersonaSettings`); all tests PASS.

- [ ] **Step 5: Smoke-run the app** (verifies DI graph boots and `personas.json` is written)

Clean-rebuild the Desktop project and launch the freshly built exe (per CLAUDE.md stale-exe guidance):

```powershell
Remove-Item -Recurse -Force src/LocalDictation.Desktop/obj, src/LocalDictation.Desktop/bin -ErrorAction SilentlyContinue
dotnet build src/LocalDictation.Desktop/LocalDictation.Desktop.csproj -c Debug --nologo
Start-Process src/LocalDictation.Desktop/bin/Debug/net8.0-windows/LocalDictation.exe
```

Expected: app starts (tray icon appears); `%LocalAppData%\LocalDictation\personas.json` now exists and contains the seed personas. Close the app.

- [ ] **Step 6: Commit**

```bash
git add src/LocalDictation.Infrastructure/DependencyInjection/InfrastructureModule.cs src/LocalDictation.Desktop/App.xaml.cs src/LocalDictation.Desktop.Avalonia/App.axaml.cs
git commit -m "$(printf 'feat(personas): register persona store, resolver and settings in DI\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 8: Second (picker) hotkey in IHotkeyService + both platform services

**Files:**
- Modify: `src/LocalDictation.Application/Abstractions/WindowsAbstractions.cs`
- Modify: `src/LocalDictation.Infrastructure.Windows/Windows/HotkeyService.cs`
- Modify: `src/LocalDictation.Infrastructure.Mac/Input/CarbonHotkeyService.cs`

**Interfaces:**
- Produces: `enum HotkeyAction { Primary, Picker }`; `HotkeyPressedEventArgs` gains `HotkeyAction Action` (default `Primary`); `IHotkeyService` gains `bool RegisterPicker(string gesture)` and `void UnregisterPicker()`.

- [ ] **Step 1: Update the abstraction** (`WindowsAbstractions.cs`)

```csharp
/// <summary>Which registered hotkey fired.</summary>
public enum HotkeyAction { Primary, Picker }

/// <summary>Fired when a registered global hotkey is pressed.</summary>
public sealed class HotkeyPressedEventArgs : EventArgs
{
    /// <summary>Which hotkey fired. Primary starts/stops dictation; Picker opens the persona palette.</summary>
    public HotkeyAction Action { get; init; } = HotkeyAction.Primary;
}

public interface IHotkeyService : IDisposable
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;
    bool Register(string gesture);
    void Unregister();

    /// <summary>Registers the secondary persona-picker hotkey. Returns false if unavailable.</summary>
    bool RegisterPicker(string gesture);

    /// <summary>Removes the picker-hotkey registration.</summary>
    void UnregisterPicker();
}
```

- [ ] **Step 2: Update `HotkeyService.cs` (Windows)** — add a second id and route the action:

Add the id constant and a `_pickerRegistered` flag:

```csharp
    private const int HotkeyId = 0x0B00;
    private const int PickerHotkeyId = 0x0B01;
    private bool _registered;
    private bool _pickerRegistered;
```

Add the picker registration methods (mirror `Register`/`Unregister`):

```csharp
    /// <inheritdoc />
    public bool RegisterPicker(string gesture)
    {
        UnregisterPicker();
        if (!TryParse(gesture, out var mods, out var vk)) return false;
        _pickerRegistered = NativeMethods.RegisterHotKey(nint.Zero, PickerHotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk);
        if (_pickerRegistered) _log.LogInformation("Registered picker hotkey '{Gesture}'.", gesture);
        return _pickerRegistered;
    }

    /// <inheritdoc />
    public void UnregisterPicker()
    {
        if (_pickerRegistered) { NativeMethods.UnregisterHotKey(nint.Zero, PickerHotkeyId); _pickerRegistered = false; }
    }
```

Route the id → action in `OnThreadPreprocessMessage`:

```csharp
    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != NativeMethods.WM_HOTKEY) return;
        var id = msg.wParam.ToInt32();
        if (id == HotkeyId) { handled = true; HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs { Action = HotkeyAction.Primary }); }
        else if (id == PickerHotkeyId) { handled = true; HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs { Action = HotkeyAction.Picker }); }
    }
```

Also call `UnregisterPicker();` inside `Dispose()` (next to `Unregister()`).

- [ ] **Step 3: Update `CarbonHotkeyService.cs` (macOS)** — mirror the pattern.

Read the file first. It registers one Carbon hotkey with an event handler keyed by a hotkey id. Add a second registration (a distinct `EventHotKeyID.id`, e.g. 2) and raise `HotkeyPressedEventArgs { Action = HotkeyAction.Picker }` when that id fires; add `RegisterPicker`/`UnregisterPicker` and unregister it on dispose. Keep every new type `[SupportedOSPlatform("macos")]` consistent with the file. If the Carbon handler dispatches by `hotKeyID.id`, branch on it exactly as Windows branches on the message id.

- [ ] **Step 4: Build (both platforms compile-check)**

Run (Windows): `dotnet build LocalDictation.sln -c Debug --nologo`
Run (mac projects compile-check on Windows): `dotnet build src/LocalDictation.Infrastructure.Mac/LocalDictation.Infrastructure.Mac.csproj -c Debug --nologo`
Expected: both succeed. (The mac service is not run here; it is exercised on Mac hardware later.)

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Application/Abstractions/WindowsAbstractions.cs src/LocalDictation.Infrastructure.Windows/Windows/HotkeyService.cs src/LocalDictation.Infrastructure.Mac/Input/CarbonHotkeyService.cs
git commit -m "$(printf 'feat(personas): add secondary picker hotkey to hotkey services\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 9: Persona picker overlay (WPF) + controller wiring

**Files:**
- Create: `src/LocalDictation.Desktop/Views/PersonaPickerWindow.xaml` (+`.xaml.cs`)
- Modify: `src/LocalDictation.Desktop/Services/DictationController.cs`
- Modify: `src/LocalDictation.Desktop/App.xaml.cs` (register `PersonaPickerWindow`, pass to controller / provide a picker callback)

**Interfaces:**
- Consumes: `PersonaSettings`, `HotkeyAction`, updated `RunAsync(..., personaOverride)`.
- Produces: `PersonaPickerWindow` implementing a small `IPersonaPicker { Task<Persona?> PickAsync(); }` (define in Desktop, since it's a UI concern), returning the chosen enabled persona or null (ESC).

Design: a borderless, `AllowsTransparency` acrylic palette (reuse the `FloatingEditorWindow` glass chrome + `TextBoxStyle`/`Win11Card`), bottom-center, ~460 wide. A search `TextBox` (filters enabled personas by name, `UpdateSourceTrigger=PropertyChanged`), a `ListBox` of matches (number-keyed), Enter/click selects, ESC/lost-focus cancels. `PickAsync()` shows it modally-ish (`Show()` + focus), completes a `TaskCompletionSource<Persona?>` on select/cancel.

- [ ] **Step 1: Create `PersonaPickerWindow.xaml`**

```xml
<Window x:Class="LocalDictation.Desktop.Views.PersonaPickerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True" SizeToContent="Height" Width="460"
        WindowStartupLocation="Manual" ResizeMode="NoResize">
    <Border CornerRadius="16" Background="#F21C1B1E" BorderBrush="#22FFFFFF" BorderThickness="1">
        <StackPanel Margin="0">
            <TextBlock Text="PERSONA · CTRL+ALT+SPACE" FontSize="10.5" Margin="16,13,16,0"
                       Foreground="{StaticResource TextFaintBrush}"/>
            <TextBox x:Name="Search" Style="{StaticResource TextBoxStyle}" Margin="16,10,16,12"
                     BorderThickness="0" Background="Transparent" FontSize="15"
                     TextChanged="OnSearchChanged" PreviewKeyDown="OnKeyDown"/>
            <ListBox x:Name="List" Background="Transparent" BorderThickness="0" MaxHeight="260"
                     HorizontalContentAlignment="Stretch" SelectionChanged="OnSelectionChanged"
                     MouseDoubleClick="OnAccept">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="6,7">
                            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                            <TextBlock Text="{Binding Name}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="13"/>
                            <TextBlock Grid.Column="1" Text="{Binding MatchSummary}" FontSize="11"
                                       Foreground="{StaticResource TextFaintBrush}"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
            <TextBlock Text="↑↓ navigate · ↵ select · esc cancel · AI on for this dictation"
                       FontSize="11" Margin="16,11,16,13" Foreground="{StaticResource ProcessingBrush}"/>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: Create `PersonaPickerWindow.xaml.cs`**

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.Views;

/// <summary>UI port for choosing a persona for a single dictation.</summary>
public interface IPersonaPicker
{
    /// <summary>Shows the palette and resolves to the chosen persona, or null if cancelled.</summary>
    Task<Persona?> PickAsync();
}

/// <summary>Small view item exposing a persona's display name and match summary to the palette.</summary>
public sealed class PickerItem
{
    public required Persona Persona { get; init; }
    public string Name => Persona.Name;
    public string MatchSummary => Persona.MatchProcessNames.Count == 0
        ? "picker only" : "auto · " + string.Join(", ", Persona.MatchProcessNames);
}

/// <summary>
/// A command-palette overlay listing enabled personas. Reuses the glass chrome + monochrome styles.
/// Non-modal (Show + focus); completes a TaskCompletionSource on select or cancel.
/// </summary>
public partial class PersonaPickerWindow : Window, IPersonaPicker
{
    private readonly PersonaSettings _personas;
    private readonly ObservableCollection<PickerItem> _items = new();
    private TaskCompletionSource<Persona?>? _tcs;

    public PersonaPickerWindow(PersonaSettings personas)
    {
        _personas = personas;
        InitializeComponent();
        List.ItemsSource = _items;
    }

    /// <inheritdoc />
    public Task<Persona?> PickAsync()
    {
        _tcs = new TaskCompletionSource<Persona?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Search.Text = "";
        Rebuild("");
        PositionBottomCenter();
        Show(); Activate();
        Search.Focus();
        return _tcs.Task;
    }

    private void Rebuild(string filter)
    {
        _items.Clear();
        foreach (var p in _personas.Personas)
        {
            if (!p.Enabled) continue;
            if (!string.IsNullOrWhiteSpace(filter) && !p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            _items.Add(new PickerItem { Persona = p });
        }
        if (_items.Count > 0) List.SelectedIndex = 0;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild(Search.Text);
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Complete(null); e.Handled = true; break;
            case Key.Enter: Accept(); e.Handled = true; break;
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
        }
    }

    private void Move(int delta)
    {
        if (_items.Count == 0) return;
        var i = List.SelectedIndex + delta;
        List.SelectedIndex = Math.Clamp(i, 0, _items.Count - 1);
    }

    private void OnAccept(object sender, MouseButtonEventArgs e) => Accept();
    private void Accept() => Complete((List.SelectedItem as PickerItem)?.Persona);

    private void Complete(Persona? chosen)
    {
        Hide();
        _tcs?.TrySetResult(chosen);
        _tcs = null;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible) Complete(null); // clicking away cancels
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height - 320;
    }
}
```

- [ ] **Step 3: Register the window + expose the picker in `App.xaml.cs`**

Register `PersonaPickerWindow` as a singleton, and register `IPersonaPicker` to resolve to it:

```csharp
        services.AddSingleton<PersonaPickerWindow>();
        services.AddSingleton<IPersonaPicker>(sp => sp.GetRequiredService<PersonaPickerWindow>());
```

(Add `using LocalDictation.Desktop.Views;`.) The picker window must be created on the UI thread — since `App` builds the container on the UI thread and other windows (ControlPanel) are already singletons, this is consistent.

- [ ] **Step 4: Wire the controller** — `DictationController.cs`

Add fields + ctor params `IPersonaPicker picker` and `PersonaSettings personas` and `IUiDispatcher` (already present). Store `IPersonaPicker _picker; PersonaSettings _personas;` Add a pending-persona field:

```csharp
    private Persona? _pendingPersona;
```

In `Initialize()`, change the hotkey subscription to route by action and register the picker hotkey:

```csharp
        _hotkey.HotkeyPressed += (_, e) =>
        {
            if (e.Action == HotkeyAction.Picker) _ = OnPickerHotkeyAsync();
            else OnHotkey();
        };
```

After `RegisterHotkeyWithFallback();` add:

```csharp
        if (!string.IsNullOrWhiteSpace(_personas.PickerHotkey))
            _hotkey.RegisterPicker(_personas.PickerHotkey);
```

Add the picker flow (capture target first, show palette, start recording with the chosen persona):

```csharp
    /// <summary>Picker hotkey: choose a persona, then dictate that one session with AI forced on.</summary>
    private async Task OnPickerHotkeyAsync()
    {
        if (_recording) { _ = FinishAsync(); return; } // second press ends the in-flight dictation
        var chosen = await _picker.PickAsync();
        if (chosen is null) return; // cancelled
        _pendingPersona = chosen;
        await StartAsync();
    }
```

In `FinishAsync`, pass the pending persona into the pipeline and clear it. Change the `RunAsync` call:

```csharp
            var outcome = await _pipeline.RunAsync(clip, _target ?? TargetControl.Unknown, _settings, ct, _pendingPersona);
```

and in the `finally` block add `_pendingPersona = null;`.

> Note: `StartAsync` captures the focused target *after* the palette closes. Because the palette took focus, capture the target BEFORE showing the palette instead. Refactor: in `OnPickerHotkeyAsync`, capture the target first, stash it, and have `StartAsync` reuse it. Minimal approach — capture and stash before `PickAsync`:
> ```csharp
>     private async Task OnPickerHotkeyAsync()
>     {
>         if (_recording) { _ = FinishAsync(); return; }
>         var target = _inspector.CaptureFocusedTarget();
>         if (target.IsSensitive || IsBlocked(target)) { _notify.Info("Dictation blocked", $"{target.ProcessName} is a protected field."); return; }
>         var chosen = await _picker.PickAsync();
>         if (chosen is null) return;
>         _pendingPersona = chosen;
>         _pendingTarget = target;
>         await StartAsync();
>     }
> ```
> Add `private TargetControl? _pendingTarget;`. In `StartAsync`, when `_pendingTarget` is set, use it instead of calling `_inspector.CaptureFocusedTarget()` again, then clear it. This keeps output routed to the app that had focus when the hotkey was pressed.

- [ ] **Step 5: Build + manual run verification**

Clean-rebuild the Desktop project and launch (as in Task 7 Step 5). Then:
1. Focus a text field (e.g. Notepad).
2. Press **Ctrl+Alt+Space** → the persona palette appears bottom-center with the enabled personas.
3. Type to filter; press Enter on "Coding Agent".
4. Speak (or play `Evals/.../fixtures/f1.wav` into the mic), press the primary hotkey to finish.
5. Confirm AI enhancement ran even though the global AI toggle is off (the output is reshaped, not verbatim), and text landed in Notepad.
6. Press Ctrl+Alt+Space then ESC → palette closes, no dictation starts.

Expected: all six behave as described. If Ollama isn't installed, the pipeline degrades to raw text (acceptable) — verify no crash.

- [ ] **Step 6: Commit**

```bash
git add src/LocalDictation.Desktop/Views/PersonaPickerWindow.xaml src/LocalDictation.Desktop/Views/PersonaPickerWindow.xaml.cs src/LocalDictation.Desktop/Services/DictationController.cs src/LocalDictation.Desktop/App.xaml.cs
git commit -m "$(printf 'feat(personas): add WPF persona picker palette and hotkey wiring\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 10: Persona picker overlay (Avalonia) + mac controller wiring

**Files:**
- Create: `src/LocalDictation.Desktop.Avalonia/Views/PersonaPickerWindow.axaml` (+`.axaml.cs`)
- Modify: `src/LocalDictation.Desktop.Avalonia/Services/MacDictationController.cs`
- Modify: `src/LocalDictation.Desktop.Avalonia/App.axaml.cs`

**Interfaces:** mirrors Task 9 with Avalonia types (`Window`, `Classes`-based styling, `TextBox`, `ListBox`). Define `IPersonaPicker` in the Avalonia project (same shape) or share via the app namespace as done for other UI ports.

- [ ] **Step 1: Create the Avalonia palette** — `PersonaPickerWindow.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LocalDictation.Desktop.Avalonia.Views.PersonaPickerWindow"
        SystemDecorations="None" TransparencyLevelHint="AcrylicBlur" Background="#F21C1B1E"
        ShowInTaskbar="False" Topmost="True" SizeToContent="Height" Width="460" CanResize="False">
    <StackPanel>
        <TextBlock Text="PERSONA · CTRL+ALT+SPACE" FontSize="10.5" Margin="16,13,16,0" Foreground="#66645F"/>
        <TextBox x:Name="Search" Watermark="Search personas…" Margin="16,10,16,12" FontSize="15"
                 Background="Transparent" BorderThickness="0" TextChanged="OnSearchChanged" KeyDown="OnKeyDown"/>
        <ListBox x:Name="List" Background="Transparent" MaxHeight="260" DoubleTapped="OnAccept">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid ColumnDefinitions="*,Auto" Margin="6,7">
                        <TextBlock Text="{Binding Name}" FontSize="13" Foreground="#F4F2EF"/>
                        <TextBlock Grid.Column="1" Text="{Binding MatchSummary}" FontSize="11" Foreground="#66645F"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        <TextBlock Text="↑↓ navigate · ↵ select · esc cancel · AI on for this dictation"
                   FontSize="11" Margin="16,11,16,13" Foreground="#E8B478"/>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Create `PersonaPickerWindow.axaml.cs`** — same logic as WPF, Avalonia APIs:

```csharp
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.Avalonia.Views;

public interface IPersonaPicker { Task<Persona?> PickAsync(); }

public sealed class PickerItem
{
    public required Persona Persona { get; init; }
    public string Name => Persona.Name;
    public string MatchSummary => Persona.MatchProcessNames.Count == 0
        ? "picker only" : "auto · " + string.Join(", ", Persona.MatchProcessNames);
}

public partial class PersonaPickerWindow : Window, IPersonaPicker
{
    private readonly PersonaSettings _personas;
    private readonly ObservableCollection<PickerItem> _items = new();
    private TaskCompletionSource<Persona?>? _tcs;

    public PersonaPickerWindow(PersonaSettings personas)
    {
        _personas = personas;
        InitializeComponent();
        this.FindControl<ListBox>("List")!.ItemsSource = _items;
        Deactivated += (_, _) => { if (IsVisible) Complete(null); };
    }

    public Task<Persona?> PickAsync()
    {
        _tcs = new TaskCompletionSource<Persona?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var search = this.FindControl<TextBox>("Search")!;
        search.Text = "";
        Rebuild("");
        Show(); Activate(); search.Focus();
        return _tcs.Task;
    }

    private void Rebuild(string? filter)
    {
        _items.Clear();
        foreach (var p in _personas.Personas)
        {
            if (!p.Enabled) continue;
            if (!string.IsNullOrWhiteSpace(filter) && !p.Name.Contains(filter!, StringComparison.OrdinalIgnoreCase)) continue;
            _items.Add(new PickerItem { Persona = p });
        }
        var list = this.FindControl<ListBox>("List")!;
        if (_items.Count > 0) list.SelectedIndex = 0;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
        => Rebuild(this.FindControl<TextBox>("Search")!.Text);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var list = this.FindControl<ListBox>("List")!;
        switch (e.Key)
        {
            case Key.Escape: Complete(null); e.Handled = true; break;
            case Key.Enter: Accept(); e.Handled = true; break;
            case Key.Down: list.SelectedIndex = Math.Min(list.SelectedIndex + 1, _items.Count - 1); e.Handled = true; break;
            case Key.Up: list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.Handled = true; break;
        }
    }

    private void OnAccept(object? sender, TappedEventArgs e) => Accept();
    private void Accept() => Complete((this.FindControl<ListBox>("List")!.SelectedItem as PickerItem)?.Persona);

    private void Complete(Persona? chosen)
    {
        Hide();
        _tcs?.TrySetResult(chosen);
        _tcs = null;
    }
}
```

- [ ] **Step 3: Wire `MacDictationController.cs` + `App.axaml.cs`** — apply the same changes as Task 9 Steps 3–4 using the Avalonia `IPersonaPicker`/`PersonaPickerWindow`. Read `MacDictationController.cs` first; it mirrors the WPF controller line-for-line, so the same edits apply (route `HotkeyAction`, register picker hotkey, capture target before `PickAsync`, pass `_pendingPersona` into `RunAsync`).

- [ ] **Step 4: Build the mac projects (compile-check on Windows)**

Run: `dotnet build src/LocalDictation.Desktop.Avalonia/LocalDictation.Desktop.Avalonia.csproj -c Debug -p:EnableWindowsTargeting=true --nologo`
Expected: succeeds. (Behavioral verification happens on Mac hardware in a later on-device pass; note this in the PR.)

- [ ] **Step 5: Commit**

```bash
git add src/LocalDictation.Desktop.Avalonia/Views/PersonaPickerWindow.axaml src/LocalDictation.Desktop.Avalonia/Views/PersonaPickerWindow.axaml.cs src/LocalDictation.Desktop.Avalonia/Services/MacDictationController.cs src/LocalDictation.Desktop.Avalonia/App.axaml.cs
git commit -m "$(printf 'feat(personas): add Avalonia persona picker and mac controller wiring\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 11: Personas section — view model (WPF)

**Files:**
- Create: `src/LocalDictation.Desktop/ViewModels/PersonaRowViewModel.cs`
- Modify: `src/LocalDictation.Desktop/ViewModels/ControlPanelViewModel.cs`

**Interfaces:**
- Consumes: `PersonaSettings`, `IPersonaStore`, `PersonaSeeds`, `Persona`, `PersonaKind`.
- Produces (on `ControlPanelViewModel`): `bool PersonasAutoApply`, `ObservableCollection<PersonaRowViewModel> Personas`, `IReadOnlyList<PersonaRowViewModel> DefaultPersonaChoices`, `PersonaRowViewModel? DefaultPersona`, `string PickerHotkey`, and commands `AddPersonaCommand`, `EditPersonaCommand`, `SavePersonaCommand`, `CancelEditCommand`, `ResetPersonaCommand`, `DeletePersonaCommand`. Persists via a new `PersistPersonas()` using the injected `IPersonaStore`.

> Read `ControlPanelViewModel.cs` first to match its exact conventions: hand-written `SetProperty` properties, the `Persist()` fire-and-forget idiom, ctor DI, `ObservableCollection` usage. Inject `PersonaSettings` and `IPersonaStore` by adding them to the ctor (the container already has both from Task 3/7).

- [ ] **Step 1: Create `PersonaRowViewModel.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using LocalDictation.Domain;

namespace LocalDictation.Desktop.ViewModels;

/// <summary>One persona row in the settings list; wraps a <see cref="Persona"/> for editing.</summary>
/// <remarks>Hand-written properties (no source generators) per the WPF markup-compiler gotcha.</remarks>
public sealed class PersonaRowViewModel : ObservableObject
{
    /// <summary>The underlying persona (the source of truth persisted to personas.json).</summary>
    public Persona Model { get; }

    public PersonaRowViewModel(Persona model)
    {
        Model = model;
        _name = model.Name;
        _systemPrompt = model.SystemPrompt;
        _matchProcessNames = string.Join(", ", model.MatchProcessNames);
        _enabled = model.Enabled;
    }

    private string _name;
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Model.Name = value; } }

    private string _systemPrompt;
    public string SystemPrompt { get => _systemPrompt; set { if (SetProperty(ref _systemPrompt, value)) { Model.SystemPrompt = value; OnPropertyChanged(nameof(CharCount)); } } }

    private string _matchProcessNames;
    /// <summary>Comma-separated exe names bound to the editor; parsed back into the model on save.</summary>
    public string MatchProcessNames { get => _matchProcessNames; set => SetProperty(ref _matchProcessNames, value); }

    private bool _enabled;
    public bool Enabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) Model.Enabled = value; } }

    private bool _isEditing;
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

    /// <summary>Character count for the prompt editor's live counter (soft cap 1500).</summary>
    public int CharCount => _systemPrompt?.Length ?? 0;

    /// <summary>"Auto · notion.exe" / "Picker only" summary for the collapsed row.</summary>
    public string MatchSummary => Model.MatchProcessNames.Count == 0
        ? "Picker only" : "Auto · " + string.Join(", ", Model.MatchProcessNames);

    /// <summary>True for System/BuiltIn personas (Reset shown, Delete hidden).</summary>
    public bool CanReset => Model.Kind != PersonaKind.User;

    /// <summary>True for User personas (Delete shown).</summary>
    public bool CanDelete => Model.Kind == PersonaKind.User;

    /// <summary>Commits editor fields (parsing the match list) back into the model before persisting.</summary>
    public void CommitToModel()
    {
        Model.Name = Name;
        Model.SystemPrompt = SystemPrompt;
        Model.Enabled = Enabled;
        Model.MatchProcessNames = MatchProcessNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Persona.NormalizeProcessName).Where(s => s.Length > 0).Distinct().ToList();
        OnPropertyChanged(nameof(MatchSummary));
    }
}
```

- [ ] **Step 2: Extend `ControlPanelViewModel.cs`** — add persona state + commands (match existing style).

Add ctor params `PersonaSettings personas, IPersonaStore personaStore`, store them, and after the existing collection setup:

```csharp
        _personaSettings = personas;
        _personaStore = personaStore;
        Personas = new ObservableCollection<PersonaRowViewModel>(personas.Personas.Select(p => new PersonaRowViewModel(p)));
        _personasAutoApply = personas.AutoApply;
        _pickerHotkey = personas.PickerHotkey;
        DefaultPersonaChoices = Personas.ToList();
        _defaultPersona = Personas.FirstOrDefault(r => r.Model.Id == personas.DefaultPersonaId);

        AddPersonaCommand = new RelayCommand(AddPersona);
        EditPersonaCommand = new RelayCommand<PersonaRowViewModel>(r => { if (r != null) r.IsEditing = true; });
        CancelEditCommand = new RelayCommand<PersonaRowViewModel>(r => { if (r != null) r.IsEditing = false; });
        SavePersonaCommand = new RelayCommand<PersonaRowViewModel>(SavePersona);
        ResetPersonaCommand = new RelayCommand<PersonaRowViewModel>(ResetPersona);
        DeletePersonaCommand = new RelayCommand<PersonaRowViewModel>(DeletePersona);
```

Add the fields, properties and methods:

```csharp
    private readonly PersonaSettings _personaSettings;
    private readonly IPersonaStore _personaStore;

    public ObservableCollection<PersonaRowViewModel> Personas { get; }
    public IReadOnlyList<PersonaRowViewModel> DefaultPersonaChoices { get; private set; }

    private bool _personasAutoApply;
    public bool PersonasAutoApply { get => _personasAutoApply; set { if (SetProperty(ref _personasAutoApply, value)) { _personaSettings.AutoApply = value; PersistPersonas(); } } }

    private string _pickerHotkey;
    public string PickerHotkey { get => _pickerHotkey; set { if (SetProperty(ref _pickerHotkey, value) && !string.Equals(value, _settings.Hotkey, StringComparison.OrdinalIgnoreCase)) { _personaSettings.PickerHotkey = value; PersistPersonas(); } } }

    private PersonaRowViewModel? _defaultPersona;
    public PersonaRowViewModel? DefaultPersona { get => _defaultPersona; set { if (SetProperty(ref _defaultPersona, value)) { _personaSettings.DefaultPersonaId = value?.Model.Id; PersistPersonas(); } } }

    public IRelayCommand AddPersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> EditPersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> CancelEditCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> SavePersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> ResetPersonaCommand { get; private set; } = null!;
    public IRelayCommand<PersonaRowViewModel> DeletePersonaCommand { get; private set; } = null!;

    private void AddPersona()
    {
        var model = new Persona { Id = "user-" + Guid.NewGuid().ToString("N")[..8], Name = "New persona", Kind = PersonaKind.User };
        _personaSettings.Personas.Add(model);
        var row = new PersonaRowViewModel(model) { IsEditing = true };
        Personas.Add(row);
        PersistPersonas();
    }

    private void SavePersona(PersonaRowViewModel? row)
    {
        if (row is null) return;
        row.CommitToModel();
        row.IsEditing = false;
        RefreshDefaultChoices();
        PersistPersonas();
    }

    private void ResetPersona(PersonaRowViewModel? row)
    {
        if (row is null) return;
        var seed = PersonaSeeds.DefaultPromptFor(row.Model.Id);
        if (seed != null) row.SystemPrompt = seed; // updates model + counter
        PersistPersonas();
    }

    private void DeletePersona(PersonaRowViewModel? row)
    {
        if (row is null || row.Model.Kind != PersonaKind.User) return;
        _personaSettings.Personas.Remove(row.Model);
        Personas.Remove(row);
        if (_defaultPersona == row) DefaultPersona = Personas.FirstOrDefault(r => r.Model.Id == "general");
        RefreshDefaultChoices();
        PersistPersonas();
    }

    private void RefreshDefaultChoices()
    {
        DefaultPersonaChoices = Personas.ToList();
        OnPropertyChanged(nameof(DefaultPersonaChoices));
    }

    /// <summary>Fire-and-forget persist, mirroring <see cref="Persist"/> for settings.</summary>
    private void PersistPersonas() => _ = _personaStore.SaveAsync(_personaSettings);
```

Add `using`s: `LocalDictation.Application.Processing;` (for `PersonaSeeds`), `LocalDictation.Domain;`, `CommunityToolkit.Mvvm.Input;` (for `RelayCommand`). If the file already imports these, skip.

- [ ] **Step 3: Build**

Run: `dotnet build src/LocalDictation.Desktop/LocalDictation.Desktop.csproj -c Debug --nologo`
Expected: succeeds (VM compiles; DI will supply the new ctor args from Task 7).

- [ ] **Step 4: Commit**

```bash
git add src/LocalDictation.Desktop/ViewModels/PersonaRowViewModel.cs src/LocalDictation.Desktop/ViewModels/ControlPanelViewModel.cs
git commit -m "$(printf 'feat(personas): add persona management view model (WPF)\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 12: Personas section — XAML (WPF Control Panel)

**Files:**
- Modify: `src/LocalDictation.Desktop/Views/ControlPanelWindow.xaml`

**Interfaces:** binds to Task 11's VM members. Uses only existing styles (`GroupHeader`, `Win11Card`, `CardTitle`, `CardDesc`, `Switch`, `ComboBoxStyle`, `TextBoxStyle`, `GhostButton`, `PrimaryButton`, `BoolToVis`).

- [ ] **Step 1: Insert the Personas section** after the "AI enhancement" section's closing card and before "History". Add a `BooleanToVisibilityConverter` reference if not already present (the window already uses `BoolToVis`). Paste:

```xml
<!-- ================= Personas ================= -->
<Grid>
    <TextBlock Text="Personas" Style="{StaticResource GroupHeader}"/>
    <Button Content="+ Add persona" Style="{StaticResource GhostButton}" Padding="12,4" FontSize="11.5"
            HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,0,6"
            Command="{Binding AddPersonaCommand}"/>
</Grid>

<!-- config card -->
<Border Style="{StaticResource Win11Card}" Padding="0">
    <StackPanel>
        <Grid Margin="16,12">
            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="Apply personas automatically" Style="{StaticResource CardTitle}"/>
                <TextBlock Text="Match the focused app to a persona whenever AI is on" Style="{StaticResource CardDesc}"/>
            </StackPanel>
            <CheckBox Grid.Column="1" Style="{StaticResource Switch}" VerticalAlignment="Center" IsChecked="{Binding PersonasAutoApply}"/>
        </Grid>
        <Border BorderBrush="{StaticResource BorderSoftBrush}" BorderThickness="0,1,0,0" Padding="16,11">
            <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <TextBlock Text="Default persona" Style="{StaticResource Body}" VerticalAlignment="Center"/>
                <ComboBox Grid.Column="1" Style="{StaticResource ComboBoxStyle}" MinWidth="170"
                          ItemsSource="{Binding DefaultPersonaChoices}" DisplayMemberPath="Name"
                          SelectedItem="{Binding DefaultPersona}"/>
            </Grid>
        </Border>
        <Border BorderBrush="{StaticResource BorderSoftBrush}" BorderThickness="0,1,0,0" Padding="16,11">
            <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <TextBlock Text="Persona picker hotkey" Style="{StaticResource Body}" VerticalAlignment="Center"/>
                <TextBox Grid.Column="1" Style="{StaticResource TextBoxStyle}" MinWidth="150" Text="{Binding PickerHotkey}"/>
            </Grid>
        </Border>
    </StackPanel>
</Border>

<!-- persona list -->
<ItemsControl ItemsSource="{Binding Personas}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Style="{StaticResource Win11Card}" Padding="0">
                <StackPanel>
                    <!-- collapsed row -->
                    <Grid Margin="16,12">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding Name}" Style="{StaticResource CardTitle}"/>
                            <TextBlock Text="{Binding MatchSummary}" Style="{StaticResource CardDesc}"/>
                        </StackPanel>
                        <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                            <CheckBox Style="{StaticResource Switch}" VerticalAlignment="Center" IsChecked="{Binding Enabled}"/>
                            <Button Content="Edit" Style="{StaticResource GhostButton}" Padding="12,5" Margin="8,0,0,0"
                                    Command="{Binding DataContext.EditPersonaCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                    CommandParameter="{Binding}"/>
                            <Button Content="Delete" Style="{StaticResource GhostButton}" Padding="12,5" Margin="6,0,0,0"
                                    Visibility="{Binding CanDelete, Converter={StaticResource BoolToVis}}"
                                    Command="{Binding DataContext.DeletePersonaCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                    CommandParameter="{Binding}"/>
                        </StackPanel>
                    </Grid>
                    <!-- inline editor -->
                    <Border BorderBrush="{StaticResource BorderSoftBrush}" BorderThickness="0,1,0,0" Padding="16,14"
                            Visibility="{Binding IsEditing, Converter={StaticResource BoolToVis}}">
                        <StackPanel>
                            <TextBlock Text="Persona name" Style="{StaticResource CardDesc}" Margin="0,0,0,5"/>
                            <TextBox Style="{StaticResource TextBoxStyle}" Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}"/>
                            <TextBlock Text="Auto-match apps — comma-separated exe names, blank = picker only" Style="{StaticResource CardDesc}" Margin="0,11,0,5"/>
                            <TextBox Style="{StaticResource TextBoxStyle}" Text="{Binding MatchProcessNames, UpdateSourceTrigger=PropertyChanged}"/>
                            <TextBlock Text="Persona prompt — sent as the model's system message" Style="{StaticResource CardDesc}" Margin="0,11,0,5"/>
                            <TextBox Style="{StaticResource TextBoxStyle}" AcceptsReturn="True" TextWrapping="Wrap" MinHeight="104"
                                     Text="{Binding SystemPrompt, UpdateSourceTrigger=PropertyChanged}"/>
                            <TextBlock Text="{Binding CharCount, StringFormat='{}{0} / 1500 characters'}" Style="{StaticResource CardDesc}" Margin="0,5,0,0"/>
                            <Grid Margin="0,12,0,0">
                                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                    <TextBlock Text="Enabled" Style="{StaticResource Body}" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                    <CheckBox Style="{StaticResource Switch}" IsChecked="{Binding Enabled}"/>
                                </StackPanel>
                                <StackPanel Grid.Column="2" Orientation="Horizontal">
                                    <Button Content="Reset to default" Style="{StaticResource GhostButton}" Padding="12,6"
                                            Visibility="{Binding CanReset, Converter={StaticResource BoolToVis}}"
                                            Command="{Binding DataContext.ResetPersonaCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                            CommandParameter="{Binding}"/>
                                    <Button Content="Cancel" Style="{StaticResource GhostButton}" Padding="14,6" Margin="6,0,0,0"
                                            Command="{Binding DataContext.CancelEditCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                            CommandParameter="{Binding}"/>
                                    <Button Content="Save" Style="{StaticResource PrimaryButton}" Padding="16,6" Margin="8,0,0,0"
                                            Command="{Binding DataContext.SavePersonaCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                            CommandParameter="{Binding}"/>
                                </StackPanel>
                            </Grid>
                        </StackPanel>
                    </Border>
                </StackPanel>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

- [ ] **Step 2: Build + manual run verification**

Clean-rebuild + launch the Desktop exe (Task 7 Step 5). Open Settings (tray → Settings). Verify:
1. A "Personas" section appears after "AI enhancement" with the config card + list.
2. Toggling "Apply personas automatically" and changing "Default persona" persists (reopen Settings → value retained; `personas.json` updated).
3. "Edit" expands a persona inline; editing the prompt updates the character counter; "Save" collapses it and persists; "Reset to default" restores a System/BuiltIn prompt.
4. "+ Add persona" adds a User row (with Delete); "Delete" removes it.
5. System/BuiltIn rows show "Reset to default", not "Delete".

Expected: all five behave as described. (Use the in-process `RenderTargetBitmap` screenshot hook per CLAUDE.md if a clean screenshot is needed; transparent windows don't screen-capture cleanly.)

- [ ] **Step 3: Commit**

```bash
git add src/LocalDictation.Desktop/Views/ControlPanelWindow.xaml
git commit -m "$(printf 'feat(personas): add Personas section to the WPF control panel\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 13: Personas section — Avalonia mirror

**Files:**
- Modify: `src/LocalDictation.Desktop.Avalonia/ViewModels/ControlPanelViewModel.cs`
- Modify: `src/LocalDictation.Desktop.Avalonia/Views/ControlPanelWindow.axaml`
- Create: `src/LocalDictation.Desktop.Avalonia/ViewModels/PersonaRowViewModel.cs`

**Interfaces:** mirrors Tasks 11–12 with Avalonia `Classes`-based styles (`card`, `group`, `title`, `desc`, `ghost`, `primary`) and native `ToggleSwitch`/`ComboBox`/`TextBox`.

- [ ] **Step 1: Port the row VM + control-panel VM additions** — copy Task 11's `PersonaRowViewModel.cs` and `ControlPanelViewModel` additions into the Avalonia project namespaces (`LocalDictation.Desktop.Avalonia.ViewModels`). The VM logic is identical (no WPF types). Confirm the Avalonia `ControlPanelViewModel` ctor gains `PersonaSettings` + `IPersonaStore` (registered from Task 7's `App.axaml.cs`).

- [ ] **Step 2: Add the Avalonia XAML section** after "AI enhancement" in `ControlPanelWindow.axaml`:

```xml
<TextBlock Classes="group" Text="Personas"/>
<Border Classes="card">
  <StackPanel Spacing="10">
    <Grid ColumnDefinitions="*,Auto">
      <StackPanel><TextBlock Classes="title" Text="Apply personas automatically"/>
        <TextBlock Classes="desc" Text="Match the focused app to a persona whenever AI is on"/></StackPanel>
      <ToggleSwitch Grid.Column="1" OnContent="" OffContent="" IsChecked="{Binding PersonasAutoApply}"/>
    </Grid>
    <Grid ColumnDefinitions="*,220"><TextBlock Classes="desc" Text="Default persona" VerticalAlignment="Center"/>
      <ComboBox Grid.Column="1" ItemsSource="{Binding DefaultPersonaChoices}" SelectedItem="{Binding DefaultPersona}">
        <ComboBox.ItemTemplate><DataTemplate><TextBlock Text="{Binding Name}"/></DataTemplate></ComboBox.ItemTemplate></ComboBox></Grid>
    <Grid ColumnDefinitions="*,220"><TextBlock Classes="desc" Text="Persona picker hotkey" VerticalAlignment="Center"/>
      <TextBox Grid.Column="1" Text="{Binding PickerHotkey}"/></Grid>
  </StackPanel>
</Border>
<Button Classes="ghost" Content="+ Add persona" HorizontalAlignment="Right" Command="{Binding AddPersonaCommand}"/>
<ItemsControl ItemsSource="{Binding Personas}">
  <ItemsControl.ItemTemplate><DataTemplate>
    <Border Classes="card">
      <StackPanel Spacing="8">
        <Grid ColumnDefinitions="*,Auto">
          <StackPanel><TextBlock Classes="title" Text="{Binding Name}"/><TextBlock Classes="desc" Text="{Binding MatchSummary}"/></StackPanel>
          <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
            <ToggleSwitch OnContent="" OffContent="" IsChecked="{Binding Enabled}"/>
            <Button Classes="ghost" Content="Edit"
                    Command="{Binding $parent[ItemsControl].DataContext.EditPersonaCommand}" CommandParameter="{Binding}"/>
            <Button Classes="ghost" Content="Delete" IsVisible="{Binding CanDelete}"
                    Command="{Binding $parent[ItemsControl].DataContext.DeletePersonaCommand}" CommandParameter="{Binding}"/>
          </StackPanel>
        </Grid>
        <StackPanel Spacing="6" IsVisible="{Binding IsEditing}">
          <TextBlock Classes="desc" Text="Persona name"/><TextBox Text="{Binding Name}"/>
          <TextBlock Classes="desc" Text="Auto-match apps — comma-separated, blank = picker only"/><TextBox Text="{Binding MatchProcessNames}"/>
          <TextBlock Classes="desc" Text="Persona prompt"/>
          <TextBox AcceptsReturn="True" TextWrapping="Wrap" MinHeight="104" Text="{Binding SystemPrompt}"/>
          <TextBlock Classes="desc" Text="{Binding CharCount, StringFormat='{}{0} / 1500 characters'}"/>
          <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button Classes="ghost" Content="Reset to default" IsVisible="{Binding CanReset}"
                    Command="{Binding $parent[ItemsControl].DataContext.ResetPersonaCommand}" CommandParameter="{Binding}"/>
            <Button Classes="ghost" Content="Cancel"
                    Command="{Binding $parent[ItemsControl].DataContext.CancelEditCommand}" CommandParameter="{Binding}"/>
            <Button Classes="primary" Content="Save"
                    Command="{Binding $parent[ItemsControl].DataContext.SavePersonaCommand}" CommandParameter="{Binding}"/>
          </StackPanel>
        </StackPanel>
      </StackPanel>
    </Border>
  </DataTemplate></ItemsControl.ItemTemplate>
</ItemsControl>
```

- [ ] **Step 3: Build the Avalonia project (compile-check on Windows)**

Run: `dotnet build src/LocalDictation.Desktop.Avalonia/LocalDictation.Desktop.Avalonia.csproj -c Debug -p:EnableWindowsTargeting=true --nologo`
Expected: succeeds. (Live verification on Mac hardware in the on-device pass.)

- [ ] **Step 4: Commit**

```bash
git add src/LocalDictation.Desktop.Avalonia/ViewModels/PersonaRowViewModel.cs src/LocalDictation.Desktop.Avalonia/ViewModels/ControlPanelViewModel.cs src/LocalDictation.Desktop.Avalonia/Views/ControlPanelWindow.axaml
git commit -m "$(printf 'feat(personas): mirror Personas section in Avalonia control panel\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 14: Import / Export personas

**Files:**
- Modify: `src/LocalDictation.Desktop/ViewModels/ControlPanelViewModel.cs` (+ code-behind hooks in `ControlPanelWindow.xaml.cs` for the file dialogs), `src/LocalDictation.Desktop/Views/ControlPanelWindow.xaml` (footer buttons).
- Modify: Avalonia equivalents.

**Interfaces:** Import reads a `PersonaSettings` (or a bare `List<Persona>`) from a chosen `.json`, merges by id (imported User personas added; matching ids update the prompt for User, ignored for System/BuiltIn to protect seeds), re-slugs id collisions, caps prompt length at 4000 chars, persists, and rebuilds the `Personas` collection. Export writes the current `personas.json` content to a chosen path.

- [ ] **Step 1: Add import/export methods to the VM**

```csharp
    /// <summary>Serializes current personas to <paramref name="path"/> (same shape as personas.json).</summary>
    public async Task ExportAsync(string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(_personaSettings,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    /// <summary>Merges personas from <paramref name="path"/>: adds User personas, updates existing User
    /// prompts by id, never overwrites System/BuiltIn seeds, caps prompt length. Rebuilds the list.</summary>
    public async Task ImportAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var incoming = System.Text.Json.JsonSerializer.Deserialize<PersonaSettings>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (incoming?.Personas is null) return;

        foreach (var p in incoming.Personas)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.SystemPrompt)) continue;
            if (p.SystemPrompt.Length > 4000) p.SystemPrompt = p.SystemPrompt[..4000];
            var existing = _personaSettings.FindById(p.Id);
            if (existing is null)
            {
                p.Kind = PersonaKind.User; // imported personas are always User
                _personaSettings.Personas.Add(p);
            }
            else if (existing.Kind == PersonaKind.User)
            {
                existing.Name = p.Name; existing.SystemPrompt = p.SystemPrompt;
                existing.MatchProcessNames = p.MatchProcessNames; existing.Enabled = p.Enabled;
            }
            // System/BuiltIn seeds are never overwritten by import.
        }
        Personas.Clear();
        foreach (var m in _personaSettings.Personas) Personas.Add(new PersonaRowViewModel(m));
        RefreshDefaultChoices();
        PersistPersonas();
    }
```

- [ ] **Step 2: Add footer buttons (WPF XAML)** after the persona `ItemsControl`:

```xml
<StackPanel Orientation="Horizontal" Margin="0,8,0,0">
    <Button Content="Import…" Style="{StaticResource GhostButton}" Padding="14,6" Click="OnImportPersonas"/>
    <Button Content="Export…" Style="{StaticResource GhostButton}" Padding="14,6" Margin="8,0,0,0" Click="OnExportPersonas"/>
    <TextBlock Text="Stored in personas.json" Style="{StaticResource CardDesc}" VerticalAlignment="Center" Margin="14,0,0,0"/>
</StackPanel>
```

- [ ] **Step 3: Add the file-dialog handlers in `ControlPanelWindow.xaml.cs`**

```csharp
    private async void OnImportPersonas(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Personas (*.json)|*.json", Title = "Import personas" };
        if (dlg.ShowDialog() == true && DataContext is ControlPanelViewModel vm)
            await vm.ImportAsync(dlg.FileName);
    }

    private async void OnExportPersonas(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Personas (*.json)|*.json", FileName = "personas.json", Title = "Export personas" };
        if (dlg.ShowDialog() == true && DataContext is ControlPanelViewModel vm)
            await vm.ExportAsync(dlg.FileName);
    }
```

- [ ] **Step 4: Avalonia equivalents** — add the same two methods to the Avalonia VM, add footer buttons to `ControlPanelWindow.axaml`, and use Avalonia `StorageProvider` in code-behind:

```csharp
    private async void OnExportPersonas(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Export personas", SuggestedFileName = "personas.json" });
        if (file != null && DataContext is ControlPanelViewModel vm) await vm.ExportAsync(file.Path.LocalPath);
    }

    private async void OnImportPersonas(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import personas", AllowMultiple = false });
        if (files.Count > 0 && DataContext is ControlPanelViewModel vm) await vm.ImportAsync(files[0].Path.LocalPath);
    }
```

- [ ] **Step 5: Build + manual verify (WPF)**

Clean-rebuild + launch. In Settings: Export → save `personas.json`; edit it (change a User persona prompt) or add a persona; Import it back → the change appears; System/BuiltIn seeds are untouched. Build the Avalonia project for compile-check.

Run: `dotnet build src/LocalDictation.Desktop.Avalonia/LocalDictation.Desktop.Avalonia.csproj -c Debug -p:EnableWindowsTargeting=true --nologo`

- [ ] **Step 6: Commit**

```bash
git add src/LocalDictation.Desktop/ViewModels/ControlPanelViewModel.cs src/LocalDictation.Desktop/Views/ControlPanelWindow.xaml src/LocalDictation.Desktop/Views/ControlPanelWindow.xaml.cs src/LocalDictation.Desktop.Avalonia/ViewModels/ControlPanelViewModel.cs src/LocalDictation.Desktop.Avalonia/Views/ControlPanelWindow.axaml src/LocalDictation.Desktop.Avalonia/Views/ControlPanelWindow.axaml.cs
git commit -m "$(printf 'feat(personas): import/export personas as portable json\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Task 15: Docs, ADR, onboarding note

**Files:**
- Create: `docs/adr/0018-context-aware-personas.md`
- Modify: `docs/index.html` (docs site — add a Personas subsection), `README.md` (feature + picker hotkey), `CLAUDE.md` (feature-state note).

- [ ] **Step 1: Write ADR 0018** — capture the decision: personas as data with process-name auto-match + a picker for ambiguous browser/terminal cases; legacy prompts unified as editable System personas; `personas.json` storage; the persona-never-gates-AI ladder; long-dictation safety subset now, chunk engine deferred. Follow the format of an existing file in `docs/adr/` (read one first, match its headers: Context / Decision / Consequences).

- [ ] **Step 2: Update `README.md`** — add personas to the feature list and document both hotkeys (primary `Ctrl+Shift+Space`, picker `Ctrl+Alt+Space`), plus the "personas require AI; the picker force-enables AI for one dictation" rule, and where personas are stored / how to share them.

- [ ] **Step 3: Update `docs/index.html`** — add a short "Personas" subsection describing auto-detect + picker and pointing at the `personas.json` format so contributors can PR new app personas. Match the existing page's structure/styles (do not restyle the page).

- [ ] **Step 4: Update `CLAUDE.md`** — under "Current feature state", add a line noting the persona system shipped (auto + picker, editable prompts, personas.json), and note the deferred long-dictation chunk engine.

- [ ] **Step 5: Final full build + test + smoke**

Run: `dotnet build LocalDictation.sln -c Debug --nologo` then `dotnet test LocalDictation.sln --nologo`
Expected: build succeeds; all tests PASS (original 17 + the new persona tests). Smoke-run the app once more (Task 7 Step 5) to confirm end-to-end.

- [ ] **Step 6: Commit**

```bash
git add docs/adr/0018-context-aware-personas.md README.md docs/index.html CLAUDE.md
git commit -m "$(printf 'docs(personas): ADR, README, docs site and feature-state notes\n\nCo-Authored-By: Claude <noreply@anthropic.com>')"
```

---

## Self-Review

**Spec coverage:**
- §4 data model → Tasks 1–2 (Persona, PersonaKind, PersonaSettings, seeds incl. System/BuiltIn/User kinds).
- §5 storage → Task 3 (JsonPersonaStore, AppPaths.PersonasFile).
- §6 resolution + persona-never-gates-AI ladder + Cleanup-mode unification → Task 4 (PersonaResolver, mode→System-persona mapping, AutoApply-off path).
- §7 pipeline integration + systemPromptOverride + raw-preservation hardening + oversized hint → Tasks 5–6.
- §8 hotkeys + picker → Tasks 8–10.
- §9 Control Panel section + inline editor + Reset-to-default + import/export → Tasks 11–14.
- §10 prompt-size counter → Task 11 (`CharCount`) + Task 12 (counter label).
- §11 long-dictation safety subset (num_ctx, timeout, pre-flight hint, raw-preservation) → Tasks 5–6. Full chunk engine intentionally deferred (own spec) — not in this plan by design.
- §12 edge cases → covered: sensitive/blocked (Task 9 picker path guard), default deleted → reset (Task 11 `DeletePersona`), reset-to-default (Task 11), picker-hotkey==primary rejected (Task 11 `PickerHotkey` setter guard), macOS casing (Task 1 normalize).
- §13 extensibility (data-driven new apps) → satisfied by the data model; no code path hard-codes an app.
- §15 production-readiness (ADR, docs, curated built-ins, no telemetry) → Tasks 2 (built-ins) + 15 (docs/ADR).

**Placeholder scan:** No "TBD"/"handle errors"/"similar to Task N" — every code step carries real code. Two explicit "read the file first" notes (Task 6 test constructors, Tasks 7/11 exact VM/boot idioms) are deliberate accuracy guards, not placeholders; each still specifies exactly what to add.

**Type consistency:** `Persona`, `PersonaKind`, `PersonaSettings`, `PersonaSeeds` (`CreateDefaults`/`DefaultPersonas`/`DefaultPromptFor`/`PersonaIdForMode`), `IPersonaStore`/`JsonPersonaStore`, `IPersonaResolver`/`PersonaDecision`/`Decide`, `HotkeyAction`/`RegisterPicker`, `IPersonaPicker.PickAsync`, `PersonaRowViewModel`, `RunAsync(..., Persona? personaOverride)`, `ProcessAsync(..., string? systemPromptOverride, ct)`, `DictationOutcome(..., bool Oversized)` are used identically across tasks.

**Known follow-ups (out of scope, by design):** full long-dictation chunk/merge engine (own spec); window-title sub-app matching; per-persona model/temperature; a visible in-overlay oversized banner (currently a log-only hint).
