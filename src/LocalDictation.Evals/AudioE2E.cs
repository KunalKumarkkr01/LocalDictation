using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Infrastructure;
using LocalDictation.Infrastructure.Speech;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace LocalDictation.Evals;

/// <summary>
/// End-to-end audio test: plays a known sentence through the default speaker while capturing it
/// through the real microphone pipeline (WaveInEvent, 16 kHz mono), then transcribes the captured
/// audio with the actual Whisper engine. Proves the mic → capture → Whisper path with live audio.
/// </summary>
/// <remarks>
/// Requires speakers audible to the mic. If the room/speaker path is silent, the captured level
/// stays at the noise floor and this reports that honestly rather than a false pass.
/// </remarks>
public static class AudioE2E
{
    /// <summary>Runs the acoustic capture→transcribe test.</summary>
    public static async Task RunAsync()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var paths = new AppPaths();
        var settings = new AppSettings { Language = "en", WhisperModel = SpeechModelSize.Base };
        using var http = new HttpClient();
        var models = new SpeechModelManager(paths.ModelsDir, http, loggerFactory.CreateLogger<SpeechModelManager>());
        var engine = new WhisperNetEngine(models, settings, loggerFactory.CreateLogger<WhisperNetEngine>());

        const string reference = "The quick brown fox jumps over the lazy dog.";
        var wav = Path.Combine(AppContext.BaseDirectory, "fixtures", "f1.wav");
        if (!File.Exists(wav)) { SpeechFixtures.Build(Path.Combine(AppContext.BaseDirectory, "fixtures")); }

        Console.WriteLine("=== Acoustic mic e2e: playing sentence through speaker, capturing via mic ===");
        Console.WriteLine($"  reference: \"{reference}\"");

        var captured = new List<float>();
        double maxRms = 0;
        using var mic = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), DeviceNumber = -1, BufferMilliseconds = 50 };
        mic.DataAvailable += (_, e) =>
        {
            double sumSq = 0; int n = e.BytesRecorded / 2;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                float f = s / 32768f;
                captured.Add(f);
                sumSq += f * f;
            }
            if (n > 0) maxRms = Math.Max(maxRms, Math.Sqrt(sumSq / n));
        };

        mic.StartRecording();
        using (var outDev = new WaveOutEvent())
        using (var reader = new AudioFileReader(wav))
        {
            outDev.Init(reader);
            outDev.Volume = 1.0f;
            outDev.Play();
            while (outDev.PlaybackState == PlaybackState.Playing) await Task.Delay(100);
        }
        await Task.Delay(300);
        mic.StopRecording();
        await Task.Delay(200);

        Console.WriteLine($"  captured {captured.Count / 16000.0:F1}s, maxRms={maxRms:F4}");
        if (maxRms < 0.004)
        {
            Console.WriteLine("  RESULT: mic did not pick up the speaker (silent/muted path) — cannot verify acoustically here.");
            Console.WriteLine("  (Whisper transcription of clean audio is separately proven at 0% WER by `dotnet run` evals.)");
            await engine.DisposeAsync();
            return;
        }

        await engine.WarmUpAsync();
        var result = await engine.TranscribeAsync(new AudioClip(captured.ToArray()), new TranscribeOptions("en"));
        await engine.DisposeAsync();

        if (!result.IsSuccess) { Console.WriteLine($"  transcription FAILED: {result.Error}"); return; }
        var hyp = result.Value!.RawText.Trim();
        var wer = WerCalculator.Compute(reference, hyp);
        Console.WriteLine($"  transcript: \"{hyp}\"");
        Console.WriteLine($"  WER: {wer:P0}   → {(wer <= 0.3 ? "PASS (mic path transcribes real audio)" : "HIGH WER — mic audio quality low")}");
    }
}
