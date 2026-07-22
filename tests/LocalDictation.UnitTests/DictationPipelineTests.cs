using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Pipeline;
using LocalDictation.Application.Processing;
using LocalDictation.Domain;
using LocalDictation.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LocalDictation.UnitTests;

/// <summary>
/// Behavioural tests for <see cref="DictationPipeline"/> using mocked ports. Verifies the
/// orchestration contract: transcribe → optional AI → deliver → history, plus graceful
/// degradation when the LLM is unavailable (FR-10) and cancellation on empty audio.
/// </summary>
public class DictationPipelineTests
{
    private readonly Mock<ISpeechEngine> _speech = new();
    private readonly Mock<ITextProcessor> _processor = new();
    private readonly Mock<IOutputRouter> _router = new();
    private readonly Mock<IHistoryRepository> _history = new();

    // Empty PersonaSettings: no seeded personas means PersonaResolver never auto-applies one, so
    // these pre-persona tests keep exercising DefaultMode/raw-fallback mechanics unchanged.
    private DictationPipeline Build() => new(
        _speech.Object, _processor.Object, _router.Object, _history.Object,
        new PersonaResolver(), new PersonaSettings(),
        NullLogger<DictationPipeline>.Instance);

    private static AudioClip NonEmptyClip() => new(new float[16_000]); // 1s of silence-shaped buffer
    private static TargetControl EditableTarget() => new() { ProcessName = "notepad", IsEditable = true, Kind = ControlKind.EditableTextBox };

    private void SetupTranscription(string text) =>
        _speech.Setup(s => s.TranscribeAsync(It.IsAny<AudioClip>(), It.IsAny<TranscribeOptions>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<Transcript>.Ok(new Transcript { RawText = text }));

    [Fact]
    public async Task Delivers_text_and_saves_history_on_success()
    {
        SetupTranscription("hello world");
        _router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<TargetControl>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(OutputResult.Ok("clipboard"));

        var settings = new AppSettings { AiEnabled = false, HistoryEnabled = true };
        var outcome = await Build().RunAsync(NonEmptyClip(), EditableTarget(), settings, CancellationToken.None);

        Assert.True(outcome.Delivered);
        Assert.Equal(SessionState.Completed, outcome.Session.State);
        _history.Verify(h => h.AddAsync(It.IsAny<HistoryEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Skips_ai_when_disabled()
    {
        SetupTranscription("raw text");
        _router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<TargetControl>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(OutputResult.Ok("clipboard"));

        var settings = new AppSettings { AiEnabled = false };
        await Build().RunAsync(NonEmptyClip(), EditableTarget(), settings, CancellationToken.None);

        _processor.Verify(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<ProcessingMode>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Falls_back_to_raw_text_when_llm_unavailable()
    {
        SetupTranscription("raw dictation");
        _processor.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        string? delivered = null;
        _router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<TargetControl>(), It.IsAny<CancellationToken>()))
               .Callback<string, TargetControl, CancellationToken>((t, _, _) => delivered = t)
               .ReturnsAsync(OutputResult.Ok("clipboard"));

        var settings = new AppSettings { AiEnabled = true, DefaultMode = ProcessingMode.GrammarCorrection };
        await Build().RunAsync(NonEmptyClip(), EditableTarget(), settings, CancellationToken.None);

        Assert.Equal("raw dictation", delivered); // unchanged: AI degraded gracefully
    }

    [Fact]
    public async Task Applies_ai_when_available()
    {
        SetupTranscription("raw");
        _processor.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _processor.Setup(p => p.ProcessAsync("raw", ProcessingMode.GrammarCorrection, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Result<string>.Ok("Raw."));
        string? delivered = null;
        _router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<TargetControl>(), It.IsAny<CancellationToken>()))
               .Callback<string, TargetControl, CancellationToken>((t, _, _) => delivered = t)
               .ReturnsAsync(OutputResult.Ok("clipboard"));

        var settings = new AppSettings { AiEnabled = true, DefaultMode = ProcessingMode.GrammarCorrection };
        await Build().RunAsync(NonEmptyClip(), EditableTarget(), settings, CancellationToken.None);

        Assert.Equal("Raw.", delivered);
    }

    [Fact]
    public async Task Empty_audio_is_cancelled_and_not_delivered()
    {
        var outcome = await Build().RunAsync(new AudioClip(Array.Empty<float>()), EditableTarget(), new AppSettings(), CancellationToken.None);

        Assert.False(outcome.Delivered);
        Assert.Equal(SessionState.Cancelled, outcome.Session.State);
        _router.Verify(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<TargetControl>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Transcription_failure_reports_failed_state()
    {
        _speech.Setup(s => s.TranscribeAsync(It.IsAny<AudioClip>(), It.IsAny<TranscribeOptions>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<Transcript>.Fail("engine error"));

        var outcome = await Build().RunAsync(NonEmptyClip(), EditableTarget(), new AppSettings(), CancellationToken.None);

        Assert.False(outcome.Delivered);
        Assert.Equal(SessionState.Failed, outcome.Session.State);
    }
}
