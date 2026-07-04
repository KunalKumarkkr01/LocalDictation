using LocalDictation.Domain;

namespace LocalDictation.Application.Processing;

/// <summary>
/// Builds the system+user prompts for each <see cref="ProcessingMode"/>.
/// </summary>
/// <remarks>
/// Prompts are deliberately constrained (return only the transformed text, no preamble)
/// so small edge models behave deterministically. Used by LLM-backed text processors.
/// </remarks>
public static class PromptTemplates
{
    /// <summary>Returns a system prompt that pins the model to a bare-output edit task.</summary>
    public static string SystemPrompt(ProcessingMode mode, string targetLanguage) => mode switch
    {
        ProcessingMode.GrammarCorrection =>
            "You are a dictation cleanup engine. Fix grammar, spelling, punctuation and " +
            "capitalization in the user's transcribed speech WITHOUT changing meaning, wording style, " +
            "or adding content. Output ONLY the corrected text with no quotes or commentary.",
        ProcessingMode.ProfessionalRewrite =>
            "Rewrite the user's transcribed speech in clear, concise, professional English. " +
            "Preserve all facts and intent. Output ONLY the rewritten text.",
        ProcessingMode.Translate =>
            $"Translate the user's transcribed speech into {targetLanguage}. Preserve meaning and tone. " +
            "Output ONLY the translation.",
        ProcessingMode.Summarize =>
            "Summarize the user's transcribed speech into a short, faithful summary. Output ONLY the summary.",
        ProcessingMode.MarkdownFormat =>
            "Format the user's transcribed speech as clean Markdown (headings, lists, code blocks where " +
            "appropriate) without changing the wording. Output ONLY the Markdown.",
        _ => "Return the user's text unchanged. Output ONLY the text."
    };

    /// <summary>Builds the user turn, embedding a custom template when supplied.</summary>
    public static string UserPrompt(string text, ProcessingMode mode, string? customPrompt)
    {
        if (mode == ProcessingMode.Custom && !string.IsNullOrWhiteSpace(customPrompt))
            return customPrompt.Contains("{text}", StringComparison.OrdinalIgnoreCase)
                ? customPrompt.Replace("{text}", text, StringComparison.OrdinalIgnoreCase)
                : $"{customPrompt}\n\n{text}";
        return text;
    }
}
