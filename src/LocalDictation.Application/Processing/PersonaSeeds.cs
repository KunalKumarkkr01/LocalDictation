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
