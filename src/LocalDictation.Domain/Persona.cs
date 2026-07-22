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
