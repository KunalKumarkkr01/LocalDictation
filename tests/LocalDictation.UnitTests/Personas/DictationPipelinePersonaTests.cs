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
