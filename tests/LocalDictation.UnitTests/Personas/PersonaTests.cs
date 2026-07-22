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
