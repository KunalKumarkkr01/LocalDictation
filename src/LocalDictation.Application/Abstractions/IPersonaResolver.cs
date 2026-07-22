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
