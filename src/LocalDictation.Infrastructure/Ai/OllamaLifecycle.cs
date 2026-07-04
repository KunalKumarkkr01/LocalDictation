using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Ai;

/// <summary>
/// Drives the Ollama backend behind the AI Enhancement toggle: probes the local service, starts
/// <c>ollama serve</c> if it isn't running, and loads the model into memory, reporting each step.
/// </summary>
/// <remarks>
/// Everything runs on <c>localhost</c>. Starting the service is a hidden background process; the
/// model is loaded by issuing a tiny generate with a long <c>keep_alive</c> so subsequent
/// dictations are fast. Disabling issues <c>keep_alive: 0</c> to release the model.
/// </remarks>
public sealed class OllamaLifecycle : IOllamaLifecycle
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly ILogger<OllamaLifecycle> _log;
    private OllamaStatus _status = OllamaStatus.Off;

    /// <summary>Creates the lifecycle service.</summary>
    public OllamaLifecycle(HttpClient http, AppSettings settings, ILogger<OllamaLifecycle> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(settings.OllamaUrl);
    }

    /// <inheritdoc />
    public OllamaStatus Status => _status;

    /// <inheritdoc />
    public event EventHandler<(OllamaStatus Status, string Message)>? StatusChanged;

    private void Set(OllamaStatus status, string message)
    {
        _status = status;
        _log.LogInformation("Ollama lifecycle: {Status} — {Message}", status, message);
        StatusChanged?.Invoke(this, (status, message));
    }

    /// <inheritdoc />
    public async Task<bool> EnableAsync(string model, CancellationToken ct = default)
    {
        try
        {
            if (!await IsRunningAsync(ct))
            {
                Set(OllamaStatus.Starting, "Starting Ollama…");
                if (!TryStartServer())
                {
                    Set(OllamaStatus.Failed, "Ollama is not installed. Install it from ollama.com.");
                    return false;
                }
                if (!await WaitUntilRunningAsync(TimeSpan.FromSeconds(20), ct))
                {
                    Set(OllamaStatus.Failed, "Ollama did not start in time.");
                    return false;
                }
            }

            Set(OllamaStatus.LoadingModel, $"Loading {model}…");
            if (!await LoadModelAsync(model, ct))
            {
                Set(OllamaStatus.Failed, $"Could not load {model}. Pull it with: ollama pull {model}");
                return false;
            }

            Set(OllamaStatus.Ready, $"Ready · {model}");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Enabling Ollama failed.");
            Set(OllamaStatus.Failed, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task DisableAsync(string model, CancellationToken ct = default)
    {
        try
        {
            // Release the model from memory immediately (keep_alive: 0).
            await _http.PostAsJsonAsync("/api/generate",
                new GenerateRequest { Model = model, Prompt = "", KeepAlive = "0", Stream = false }, ct);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Model release call failed (harmless)."); }
        Set(OllamaStatus.Off, "AI enhancement off");
    }

    private async Task<bool> IsRunningAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync("/api/version", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private bool TryStartServer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ollama", "serve")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not launch 'ollama serve'.");
            return false;
        }
    }

    private async Task<bool> WaitUntilRunningAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsRunningAsync(ct)) return true;
            await Task.Delay(500, ct);
        }
        return false;
    }

    private async Task<bool> LoadModelAsync(string model, CancellationToken ct)
    {
        try
        {
            // A tiny generate loads the model into RAM and pins it with keep_alive.
            using var resp = await _http.PostAsJsonAsync("/api/generate",
                new GenerateRequest { Model = model, Prompt = "hi", KeepAlive = "15m", Stream = false }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Model load failed for {Model}", model);
            return false;
        }
    }

    private sealed class GenerateRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("keep_alive")] public string KeepAlive { get; set; } = "15m";
    }
}
