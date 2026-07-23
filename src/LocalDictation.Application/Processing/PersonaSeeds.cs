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
        // Prompts are deliberately terse: one imperative task, 1-2 key constraints, and a single
        // hard "output only X" — tuned + tested against the default phi3.5:3.8b so the small model
        // stays on task without inventing content or emitting preamble.
        new Persona { Id = "general", Name = "General cleanup", Kind = PersonaKind.System,
            SystemPrompt = "Fix grammar, spelling, punctuation, and capitalization in the text, and remove " +
                "filler words (um, uh, like, you know) and false starts. Otherwise keep the original wording, " +
                "meaning, and tone. Output only the corrected text." },
        new Persona { Id = "professional", Name = "Professional rewrite", Kind = PersonaKind.System,
            SystemPrompt = "Rewrite the text in clear, concise, professional English, preserving all facts and " +
                "intent. Output only the rewrite." },
        new Persona { Id = "summarize", Name = "Summarize", Kind = PersonaKind.System,
            SystemPrompt = "Summarize the text faithfully in one to three sentences. Output only the summary." },
        new Persona { Id = "markdown", Name = "Markdown", Kind = PersonaKind.System,
            SystemPrompt = "Reformat the text as clean Markdown (headings, lists, code blocks) using ONLY the " +
                "words and information given - do not add, expand, explain, or invent anything. Output only the Markdown." },

        new Persona { Id = "notion", Name = "Notion", Kind = PersonaKind.BuiltIn,
            MatchProcessNames = new() { "notion" },
            SystemPrompt = "Reformat the text as clean, well-structured Notion Markdown (headings, bullet or " +
                "numbered lists, and a table or callout only where it fits) using ONLY the information given - " +
                "do not add, expand, or invent content. Output only the Markdown." },
        new Persona { Id = "email", Name = "Email", Kind = PersonaKind.BuiltIn,
            MatchProcessNames = new() { "outlook" },
            SystemPrompt = "Rewrite the text as a professional email with a greeting, a concise body, and a " +
                "sign-off, fixing grammar and tone while keeping the intent and facts. Output only the email." },
        new Persona { Id = "teams", Name = "Teams", Kind = PersonaKind.BuiltIn,
            MatchProcessNames = new() { "ms-teams", "teams" },
            SystemPrompt = "Rewrite the text as a short, friendly-professional chat message: remove filler, keep " +
                "the meaning. Output only the message." },
        new Persona { Id = "coding-agent", Name = "Coding Agent", Kind = PersonaKind.BuiltIn,
            SystemPrompt = "Rewrite the text as a clear implementation prompt for a coding agent - a " +
                "specification, NOT code. Capture the requirements as concise numbered steps or bullets, keeping " +
                "every technical detail exact (file names, functions, APIs, values). Do not write code and do " +
                "not add scope. Output only the prompt." }
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
