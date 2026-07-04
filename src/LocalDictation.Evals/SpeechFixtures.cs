using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using LocalDictation.Domain;
using NAudio.Wave;

namespace LocalDictation.Evals;

/// <summary>A labelled evaluation clip: the ground-truth text and its synthesized audio.</summary>
/// <param name="Id">Short identifier.</param>
/// <param name="Reference">Ground-truth transcript.</param>
/// <param name="Clip">16 kHz mono audio.</param>
public readonly record struct Fixture(string Id, string Reference, AudioClip Clip);

/// <summary>
/// Builds reproducible ASR evaluation fixtures by synthesizing known sentences to 16 kHz mono
/// audio via Windows TTS, then loading them as <see cref="AudioClip"/>s.
/// </summary>
/// <remarks>
/// Using TTS makes the corpus self-contained and deterministic (no bundled audio, no network).
/// The synthesized voice is clean, so a healthy Whisper pipeline should score a low WER.
/// </remarks>
public static class SpeechFixtures
{
    private static readonly (string Id, string Text)[] Sentences =
    {
        ("f1", "The quick brown fox jumps over the lazy dog."),
        ("f2", "Please send the quarterly report to the finance team by Friday afternoon."),
        ("f3", "I would like to schedule a meeting with the design team next week."),
        ("f4", "Remember to buy milk, eggs, and bread on the way home."),
        ("f5", "The weather today is sunny with a gentle breeze from the west."),
        ("f6", "Let us review the pull request and merge it once the tests pass."),
    };

    /// <summary>Generates (or reuses cached) fixtures under <paramref name="cacheDir"/>.</summary>
    public static IReadOnlyList<Fixture> Build(string cacheDir)
    {
        Directory.CreateDirectory(cacheDir);
        using var synth = new SpeechSynthesizer();
        var format = new SpeechAudioFormatInfo(AudioClip.RequiredSampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

        var fixtures = new List<Fixture>();
        foreach (var (id, text) in Sentences)
        {
            var wav = Path.Combine(cacheDir, id + ".wav");
            if (!File.Exists(wav))
            {
                synth.SetOutputToWaveFile(wav, format);
                synth.Speak(text);
                synth.SetOutputToNull(); // release the file handle before reading it back
            }
            fixtures.Add(new Fixture(id, text, LoadClip(wav)));
        }
        synth.SetOutputToNull();
        return fixtures;
    }

    /// <summary>Reads a 16 kHz mono 16-bit WAV into an <see cref="AudioClip"/>.</summary>
    private static AudioClip LoadClip(string path)
    {
        using var reader = new WaveFileReader(path);
        var buffer = new byte[reader.Length];
        int read = reader.Read(buffer, 0, buffer.Length);
        var samples = new float[read / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
            samples[i] = s / 32768f;
        }
        return new AudioClip(samples);
    }
}
