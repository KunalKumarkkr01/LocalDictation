using System.Diagnostics;
using System.Runtime.Versioning;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Mac.Diagnostics;

/// <summary>
/// Mic-free, deterministic dictation self-test for macOS: synthesizes a known sentence with the
/// built-in <c>say</c> command at 16 kHz mono, feeds it through the real <see cref="ISpeechEngine"/>,
/// and checks that enough of the words come back. The macOS counterpart of the Windows TTS self-test;
/// isolates the transcription path from microphone/room-noise variables.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class SaySelfTest : IDictationSelfTest
{
    private const string Phrase = "The quick brown fox jumps over the lazy dog.";
    private const double PassThreshold = 0.6;

    private readonly ISpeechEngine _engine;
    private readonly ILogger<SaySelfTest> _log;

    /// <summary>Creates the self-test against the given speech engine.</summary>
    public SaySelfTest(ISpeechEngine engine, ILogger<SaySelfTest> log)
    {
        _engine = engine;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<SelfTestResult> RunAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_engine.IsReady) await _engine.WarmUpAsync(ct);
            if (!_engine.IsReady)
                return new SelfTestResult(false, "", Phrase, TimeSpan.Zero,
                    _engine.Status.Detail ?? "Speech engine is not ready.");

            var clip = await SynthesizePhraseAsync(ct);

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

    /// <summary>Synthesizes <see cref="Phrase"/> to a 16 kHz mono <see cref="AudioClip"/> via <c>say</c>.</summary>
    private static async Task<AudioClip> SynthesizePhraseAsync(CancellationToken ct)
    {
        string wav = Path.Combine(Path.GetTempPath(), $"ld-selftest-{Guid.NewGuid():N}.wav");
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/say")
            {
                UseShellExecute = false,
                ArgumentList =
                {
                    "-o", wav,
                    "--file-format=WAVE",
                    $"--data-format=LEI16@{AudioClip.RequiredSampleRate}",
                    Phrase,
                },
            };
            using (var p = Process.Start(psi)!) await p.WaitForExitAsync(ct);

            var bytes = await File.ReadAllBytesAsync(wav, ct);
            return new AudioClip(WavToFloat(bytes));
        }
        finally
        {
            try { if (File.Exists(wav)) File.Delete(wav); } catch { /* best effort */ }
        }
    }

    /// <summary>Converts a 16-bit mono PCM WAV byte array to float samples (skips the 44-byte header).</summary>
    private static float[] WavToFloat(byte[] wav)
    {
        const int header = 44;
        if (wav.Length <= header) return Array.Empty<float>();
        int count = (wav.Length - header) / 2;
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            short s = (short)(wav[header + i * 2] | (wav[header + i * 2 + 1] << 8));
            samples[i] = s / 32768f;
        }
        return samples;
    }

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
