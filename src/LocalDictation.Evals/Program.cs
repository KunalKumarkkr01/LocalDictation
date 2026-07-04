using System.Diagnostics;
using System.Text.Json;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Evals;
using LocalDictation.Infrastructure;
using LocalDictation.Infrastructure.Ai;
using LocalDictation.Infrastructure.Speech;
using Microsoft.Extensions.Logging;

// ============================================================
// LocalDictation evaluation harness
// Measures Whisper accuracy (WER) + latency (RTF) on synthesized fixtures,
// and exercises the local LLM post-processing path. Writes artifacts/eval-report.json.
// ============================================================

if (args.Length > 0 && args[0].Equals("mic", StringComparison.OrdinalIgnoreCase))
{
    LocalDictation.Evals.MicDiagnostic.Run();
    return;
}
if (args.Length > 0 && args[0].Equals("e2e", StringComparison.OrdinalIgnoreCase))
{
    await LocalDictation.Evals.AudioE2E.RunAsync();
    return;
}

Console.WriteLine("==================================================");
Console.WriteLine("        LocalDictation - Evaluation Harness");
Console.WriteLine("==================================================\n");

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
var paths = new AppPaths();
Console.WriteLine($"Models dir : {paths.ModelsDir}");

var settings = new AppSettings { Language = "en", AiEnabled = true };
using var http = new HttpClient();

var modelManager = new SpeechModelManager(paths.ModelsDir, http, loggerFactory.CreateLogger<SpeechModelManager>());
var engine = new WhisperNetEngine(modelManager, settings, loggerFactory.CreateLogger<WhisperNetEngine>());

Console.WriteLine("Generating speech fixtures via Windows TTS...");
var fixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
var fixtures = SpeechFixtures.Build(fixtureDir);
Console.WriteLine($"  {fixtures.Count} fixtures ready.\n");

var report = new EvalReport { Cores = Environment.ProcessorCount };

// ---- ASR accuracy + latency, per installed model ----
foreach (var size in new[] { SpeechModelSize.Base, SpeechModelSize.Small })
{
    if (!modelManager.IsInstalled(size))
    {
        Console.WriteLine($"[skip] {size} not installed.");
        continue;
    }

    settings.WhisperModel = size;
    await engine.WarmUpAsync();
    Console.WriteLine($"-- Whisper {size} " + new string('-', 34));

    var mr = new ModelResult { Model = size.ToString() };
    foreach (var fx in fixtures)
    {
        var result = await engine.TranscribeAsync(fx.Clip, new TranscribeOptions("en"));
        if (!result.IsSuccess) { Console.WriteLine($"  {fx.Id}: FAILED {result.Error}"); continue; }

        var t = result.Value!;
        double wer = WerCalculator.Compute(fx.Reference, t.RawText);
        mr.Fixtures.Add(new FixtureResult
        {
            Id = fx.Id, Reference = fx.Reference, Hypothesis = t.RawText,
            Wer = wer, Ms = t.TranscriptionTime.TotalMilliseconds, Rtf = t.RealTimeFactor
        });
        Console.WriteLine($"  {fx.Id}  WER {wer,5:P0}  {t.TranscriptionTime.TotalMilliseconds,6:F0} ms  RTF {t.RealTimeFactor,4:F1}x");
        Console.WriteLine($"        \"{t.RawText.Trim()}\"");
    }

    if (mr.Fixtures.Count > 0)
    {
        mr.AvgWer = mr.Fixtures.Average(f => f.Wer);
        mr.AvgRtf = mr.Fixtures.Average(f => f.Rtf);
        mr.AvgMs = mr.Fixtures.Average(f => f.Ms);
        Console.WriteLine($"  > avg WER {mr.AvgWer:P1} | avg {mr.AvgMs:F0} ms | avg RTF {mr.AvgRtf:F1}x\n");
    }
    report.Asr.Add(mr);
}

// ---- LLM post-processing ----
Console.WriteLine("-- Local LLM post-processing " + new string('-', 22));
var processor = new OllamaTextProcessor(http, settings, loggerFactory.CreateLogger<OllamaTextProcessor>());
report.LlmAvailable = await processor.IsAvailableAsync();
report.LlmModel = settings.LlmModel;

if (report.LlmAvailable)
{
    var cases = new (ProcessingMode Mode, string Input)[]
    {
        (ProcessingMode.GrammarCorrection, "i has went to the store yesterday and buyed some milk and egg"),
        (ProcessingMode.ProfessionalRewrite, "hey so basically i think we should maybe move the meeting cause im kinda busy"),
        (ProcessingMode.MarkdownFormat, "shopping list milk eggs bread and also call the dentist"),
    };
    foreach (var (mode, input) in cases)
    {
        var sw = Stopwatch.StartNew();
        var r = await processor.ProcessAsync(input, mode, "en");
        sw.Stop();
        var outText = r.IsSuccess ? r.Value! : $"(failed: {r.Error})";
        report.Llm.Add(new LlmResult { Mode = mode.ToString(), Input = input, Output = outText, Ms = sw.ElapsedMilliseconds });
        Console.WriteLine($"  [{mode}] {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"    in : {input}");
        Console.WriteLine($"    out: {outText}\n");
    }
}
else
{
    Console.WriteLine("  Ollama not available - AI enhancement skipped (transcription-only mode verified).\n");
}

await engine.DisposeAsync();

// ---- Write report ----
var artifactsDir = FindRepoArtifacts();
Directory.CreateDirectory(artifactsDir);
var reportPath = Path.Combine(artifactsDir, "eval-report.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine("==================================================");
foreach (var m in report.Asr)
    Console.WriteLine($"  {m.Model,-6}  avg WER {m.AvgWer:P1}  avg RTF {m.AvgRtf:F1}x");
Console.WriteLine($"  LLM available: {report.LlmAvailable}");
Console.WriteLine($"\nReport written to {reportPath}");

static string FindRepoArtifacts()
{
    var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
    {
        if (File.Exists(Path.Combine(dir, "LocalDictation.sln")))
            return Path.Combine(dir, "artifacts");
        dir = Directory.GetParent(dir)?.FullName;
    }
    return Path.Combine(AppContext.BaseDirectory, "artifacts");
}
