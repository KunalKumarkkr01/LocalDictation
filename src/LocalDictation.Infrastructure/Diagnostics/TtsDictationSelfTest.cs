using System.Diagnostics;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Diagnostics;

/// <summary>
/// Runs a mic-free, deterministic dictation self-test: synthesizes a known sentence with Windows TTS
/// at 16 kHz mono, feeds it through the real <see cref="ISpeechEngine"/>, and checks that enough of
/// the words come back. Isolates the transcription path from microphone/room-noise variables.
/// </summary>
public sealed class TtsDictationSelfTest : IDictationSelfTest
{
    // A clean, phonetically varied sentence — the same style as the Evals corpus.
    private const string Phrase = "The quick brown fox jumps over the lazy dog.";
    // Fraction of reference words that must appear in the transcript to count as a pass.
    private const double PassThreshold = 0.6;

    private readonly ISpeechEngine _engine;
    private readonly ILogger<TtsDictationSelfTest> _log;

    /// <summary>Creates the self-test against the given speech engine.</summary>
    public TtsDictationSelfTest(ISpeechEngine engine, ILogger<TtsDictationSelfTest> log)
    {
        _engine = engine;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<SelfTestResult> RunAsync(CancellationToken ct = default)
    {
        try
        {
            // Make sure the engine is loaded; surface a precise reason if it can't be.
            if (!_engine.IsReady) await _engine.WarmUpAsync(ct);
            if (!_engine.IsReady)
                return new SelfTestResult(false, "", Phrase, TimeSpan.Zero,
                    _engine.Status.Detail ?? "Speech engine is not ready.");

            var clip = SynthesizePhrase(ct);

            var sw = Stopwatch.StartNew();
            var asr = await _engine.TranscribeAsync(clip, new TranscribeOptions("en"), ct);
            sw.Stop();

            if (!asr.IsSuccess)
                return new SelfTestResult(false, "", Phrase, sw.Elapsed, asr.Error);

            var heard = asr.Value!.RawText ?? "";
            var passed = WordOverlap(Phrase, heard) >= PassThreshold;
            _log.LogInformation("Self-test {Result}: heard \"{Heard}\" in {Ms}ms",
                passed ? "PASSED" : "FAILED", heard, sw.ElapsedMilliseconds);
            return new SelfTestResult(passed, heard, Phrase, sw.Elapsed, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Self-test could not run.");
            return new SelfTestResult(false, "", Phrase, TimeSpan.Zero, ex.Message);
        }
    }

    /// <summary>Synthesizes <see cref="Phrase"/> to a 16 kHz mono <see cref="AudioClip"/> in memory.</summary>
    /// <remarks>
    /// Uses <c>SetOutputToAudioStream</c>, which writes raw headerless 16-bit PCM in the requested
    /// format — so the bytes are converted to float samples directly (no WAV parsing needed).
    /// </remarks>
    private static AudioClip SynthesizePhrase(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using (var synth = new SpeechSynthesizer())
        {
            var format = new SpeechAudioFormatInfo(
                AudioClip.RequiredSampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono);
            synth.SetOutputToAudioStream(ms, format);
            synth.Speak(Phrase);
            synth.SetOutputToNull();
        }
        ct.ThrowIfCancellationRequested();

        var bytes = ms.ToArray(); // raw 16-bit mono PCM at 16 kHz
        var samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            samples[i] = s / 32768f;
        }
        return new AudioClip(samples);
    }

    /// <summary>Fraction of the reference's distinct words that also appear in the transcript (0..1).</summary>
    private static double WordOverlap(string reference, string heard)
    {
        var refWords = Tokenize(reference);
        if (refWords.Count == 0) return 0;
        var heardWords = Tokenize(heard);
        int hits = refWords.Count(w => heardWords.Contains(w));
        return (double)hits / refWords.Count;
    }

    private static HashSet<string> Tokenize(string text) =>
        new(text.ToLowerInvariant()
                .Split(new[] { ' ', '.', ',', '!', '?', ';', ':', '\n', '\r', '\t' },
                       StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
}
