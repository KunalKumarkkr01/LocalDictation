using System.Diagnostics;
using System.Text;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Shared;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace LocalDictation.Infrastructure.Speech;

/// <summary>
/// <see cref="ISpeechEngine"/> backed by Whisper.net (whisper.cpp / GGML) for fast,
/// offline, CPU-first transcription.
/// </summary>
/// <remarks>
/// The <see cref="WhisperFactory"/> (which loads the model into RAM) is created once and
/// kept resident; a lightweight processor is created per request. This avoids the biggest
/// latency killer — reloading the model on every dictation.
/// </remarks>
public sealed class WhisperNetEngine : ISpeechEngine
{
    private readonly ISpeechModelManager _models;
    private readonly AppSettings _settings;
    private readonly ILogger<WhisperNetEngine> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private SpeechModelSize _loadedModel;
    // Written only under _gate; read unlocked by the Status getter (a stale/torn status read is harmless).
    private SpeechEngineStatus _status = new(SpeechReadiness.NotWarmedUp);

    /// <summary>Creates the engine.</summary>
    public WhisperNetEngine(ISpeechModelManager models, AppSettings settings, ILogger<WhisperNetEngine> log)
    {
        _models = models;
        _settings = settings;
        _log = log;
    }

    private SpeechModelSize ActiveModel => _settings.WhisperModel ?? _models.RecommendedForHardware();

    /// <inheritdoc />
    public string Name => $"Whisper.net · {ActiveModel}";

    /// <inheritdoc />
    public bool IsReady => _factory is not null && _loadedModel == ActiveModel;

    /// <inheritdoc />
    public SpeechEngineStatus Status => IsReady ? new SpeechEngineStatus(SpeechReadiness.Ready) : _status;

    /// <inheritdoc />
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { LoadUnderGate(); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _factory?.Dispose();
            _factory = null;
            _status = new SpeechEngineStatus(SpeechReadiness.NotWarmedUp);
            LoadUnderGate();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Loads the active model into a resident factory, recording a precise <see cref="Status"/> for
    /// each failure mode (missing model, unloadable native library, corrupt file). Caller holds the gate.
    /// </summary>
    private void LoadUnderGate()
    {
        if (IsReady) return;
        var size = ActiveModel;
        if (!_models.IsInstalled(size))
        {
            _log.LogWarning("Whisper model {Size} is not installed; warm-up skipped.", size);
            _status = new SpeechEngineStatus(SpeechReadiness.ModelNotInstalled,
                $"The {size} speech model isn't installed.");
            return;
        }

        _factory?.Dispose();
        _factory = null;
        var path = _models.GetModelPath(size);
        _log.LogInformation("Loading Whisper model {Size} from {Path}", size, path);
        try
        {
            _factory = WhisperFactory.FromPath(path);
            _loadedModel = size;
            _status = new SpeechEngineStatus(SpeechReadiness.Ready);
        }
        catch (Exception ex) when (IsNativeLoadFailure(ex))
        {
            _log.LogError(ex, "Whisper native library failed to load.");
            _status = new SpeechEngineStatus(SpeechReadiness.NativeLibraryMissing,
                "The speech engine's native library didn't load. Reinstalling the app usually fixes this.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Whisper model load failed.");
            _status = new SpeechEngineStatus(SpeechReadiness.LoadFailed, ex.Message);
        }
    }

    /// <summary>
    /// True when an exception indicates the whisper/ggml native library could not be located or loaded
    /// (the classic multi-file-publish trap) rather than a bad model file.
    /// </summary>
    private static bool IsNativeLoadFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is DllNotFoundException || e is BadImageFormatException) return true;
            var m = e.Message;
            if (m.Contains("native", StringComparison.OrdinalIgnoreCase) &&
                m.Contains("librar", StringComparison.OrdinalIgnoreCase))
                return true;
            if (e.InnerException is null) break;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<Result<Transcript>> TranscribeAsync(AudioClip clip, TranscribeOptions options, CancellationToken ct = default)
    {
        if (clip.IsEmpty) return Result<Transcript>.Fail("Empty audio.");

        try
        {
            if (!IsReady) await WarmUpAsync(ct);
            if (_factory is null)
                return Result<Transcript>.Fail(_status.Detail ?? "Whisper model unavailable. Download a model in Settings.");

            var lang = string.IsNullOrWhiteSpace(options.Language) ? "auto" : options.Language;
            // English-only models (e.g. base.en) cannot auto-detect; force English.
            if (lang == "auto" && _models.GetModelPath(_loadedModel).Contains(".en.", StringComparison.OrdinalIgnoreCase))
                lang = "en";
            var builder = _factory.CreateBuilder().WithLanguage(lang);
            if (options.Translate) builder = builder.WithTranslate();
            if (!string.IsNullOrWhiteSpace(options.InitialPrompt)) builder = builder.WithPrompt(options.InitialPrompt);

            await using var processor = builder.Build();

            var sb = new StringBuilder();
            double probSum = 0; int segCount = 0;
            string detectedLang = lang;
            var sw = Stopwatch.StartNew();

            await foreach (var seg in processor.ProcessAsync(clip.Samples, ct))
            {
                // Whisper emits bracketed markers for non-speech ([BLANK_AUDIO], [ Silence ],
                // (music), ♪…) — never insert those.
                if (IsNonSpeechAnnotation(seg.Text)) continue;
                sb.Append(seg.Text);
                probSum += seg.Probability;
                segCount++;
                detectedLang = seg.Language ?? detectedLang;
            }
            sw.Stop();

            var text = sb.ToString().Trim();
            var transcript = new Transcript
            {
                RawText = text,
                Confidence = segCount > 0 ? (float)(probSum / segCount) : -1f,
                Language = detectedLang,
                AudioDuration = clip.Duration,
                TranscriptionTime = sw.Elapsed
            };
            _log.LogInformation("Transcribed {Dur:F1}s audio in {Ms}ms (RTF {Rtf:F1}x)",
                clip.Duration.TotalSeconds, sw.ElapsedMilliseconds, transcript.RealTimeFactor);
            return Result<Transcript>.Ok(transcript);
        }
        catch (OperationCanceledException)
        {
            return Result<Transcript>.Fail("Cancelled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Whisper transcription error.");
            return Result<Transcript>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// True when a segment is one of Whisper's non-speech markers (a wholly bracketed /
    /// parenthesized / starred annotation, or only musical notes) rather than dictated words.
    /// </summary>
    private static bool IsNonSpeechAnnotation(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        if ((t[0] == '[' && t[^1] == ']') ||
            (t[0] == '(' && t[^1] == ')') ||
            (t[0] == '*' && t[^1] == '*'))
            return true;
        foreach (var c in t)
            if (c != '♪' && c != '♫' && c != '*' && !char.IsWhiteSpace(c))
                return false;
        return true; // only music notes / stars / whitespace
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
