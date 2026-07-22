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
