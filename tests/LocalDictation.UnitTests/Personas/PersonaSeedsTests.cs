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
        Assert.Equal("Ctrl+Shift+P", s.PickerHotkey);

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
