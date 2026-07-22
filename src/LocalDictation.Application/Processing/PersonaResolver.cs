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
