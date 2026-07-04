using System.Net.Http;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using LocalDictation.Shared;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Speech;

/// <summary>
/// Locates, downloads and verifies Whisper ggml model files, and recommends a model
/// tier appropriate for the current hardware.
/// </summary>
/// <remarks>
/// Models live under <c>&lt;baseDir&gt;/models/whisper</c> and are downloaded from the
/// public whisper.cpp Hugging Face repo on demand — never bundled, so licensing and app
/// size stay small (offline-first: files are cached locally after first download).
/// </remarks>
public sealed class SpeechModelManager : ISpeechModelManager
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    private readonly string _dir;
    private readonly HttpClient _http;
    private readonly ILogger<SpeechModelManager> _log;

    // English-first default (base.en) for best accuracy on the primary use case;
    // multilingual models (small+) unlock other languages.
    private static readonly Dictionary<SpeechModelSize, string> Files = new()
    {
        [SpeechModelSize.Tiny] = "ggml-tiny.bin",
        [SpeechModelSize.Base] = "ggml-base.en.bin",
        [SpeechModelSize.Small] = "ggml-small.bin",
        [SpeechModelSize.Medium] = "ggml-medium.bin",
        [SpeechModelSize.LargeV3] = "ggml-large-v3.bin",
    };

    /// <summary>Creates the manager rooted at <paramref name="modelsDirectory"/>.</summary>
    public SpeechModelManager(string modelsDirectory, HttpClient http, ILogger<SpeechModelManager> log)
    {
        _dir = modelsDirectory;
        _http = http;
        _log = log;
        Directory.CreateDirectory(_dir);
    }

    /// <inheritdoc />
    public string GetModelPath(SpeechModelSize size) => Path.Combine(_dir, Files[size]);

    /// <inheritdoc />
    public bool IsInstalled(SpeechModelSize size)
    {
        var p = GetModelPath(size);
        return File.Exists(p) && new FileInfo(p).Length > 1_000_000; // guard against truncated files
    }

    /// <inheritdoc />
    public IReadOnlyList<SpeechModelInfo> List() =>
        Files.Keys.Select(s =>
        {
            var p = GetModelPath(s);
            var installed = IsInstalled(s);
            return new SpeechModelInfo(s, Files[s], installed, installed ? new FileInfo(p).Length : 0);
        }).ToList();

    /// <inheritdoc />
    public async Task<Result> DownloadAsync(SpeechModelSize size, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var url = BaseUrl + Files[size];
            var dest = GetModelPath(size);
            var tmp = dest + ".part";
            _log.LogInformation("Downloading Whisper model {Size} from {Url}", size, url);

            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var fs = File.Create(tmp))
            {
                var buffer = new byte[81920];
                long read = 0; int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0) progress?.Report((double)read / total);
                }
            }
            File.Move(tmp, dest, overwrite: true);
            _log.LogInformation("Downloaded {Size} ({Mb:F0} MB)", size, new FileInfo(dest).Length / 1024d / 1024d);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Model download failed for {Size}", size);
            return Result.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public SpeechModelSize RecommendedForHardware()
    {
        var cores = Environment.ProcessorCount;
        var ramGb = GetTotalRamGb();
        // Resource-aware policy from the design doc (§6.2.1).
        if (ramGb < 8) return SpeechModelSize.Tiny;
        if (ramGb >= 16 && cores >= 12) return SpeechModelSize.Small;
        return SpeechModelSize.Base;
    }

    private static double GetTotalRamGb()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return info.TotalAvailableMemoryBytes / 1024d / 1024d / 1024d;
        }
        catch { /* fall through */ }
        return 16; // safe assumption
    }
}
