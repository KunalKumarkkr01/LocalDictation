using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using LocalDictation.Domain;
using LocalDictation.Shared;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Ai;

/// <summary>
/// <see cref="ITextProcessor"/> that refines transcripts with a local Ollama model.
/// </summary>
/// <remarks>
/// Runs entirely on <c>localhost</c> — no cloud. Uses a low temperature and constrained
/// prompts (see <see cref="PromptTemplates"/>) so small edge models return bare, deterministic
/// output. <c>keep_alive</c> keeps the model resident to slash per-request latency.
/// </remarks>
public sealed class OllamaTextProcessor : ITextProcessor
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly ILogger<OllamaTextProcessor> _log;

    /// <summary>Creates the processor. The <see cref="HttpClient"/> base address is set from settings.</summary>
    public OllamaTextProcessor(HttpClient http, AppSettings settings, ILogger<OllamaTextProcessor> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(settings.OllamaUrl);
    }

    /// <inheritdoc />
    public string Name => $"Ollama · {_settings.LlmModel}";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("/api/tags", ct);
            if (!resp.IsSuccessStatusCode) return false;
            var tags = await resp.Content.ReadFromJsonAsync<TagsResponse>(ct);
            return tags?.Models?.Any(m => m.Name is not null &&
                       m.Name.StartsWith(_settings.LlmModel.Split(':')[0], StringComparison.OrdinalIgnoreCase)) ?? false;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Ollama availability check failed.");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Result<string>> ProcessAsync(
        string text, ProcessingMode mode, string targetLanguage = "en",
        string? customPrompt = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Result<string>.Ok(text);
        if (mode == ProcessingMode.None) return Result<string>.Ok(text);

        try
        {
            var request = new ChatRequest
            {
                Model = _settings.LlmModel,
                Stream = false,
                KeepAlive = _settings.KeepModelResident ? "15m" : "0s",
                Options = new ChatOptions { Temperature = 0.2 },
                Messages = new[]
                {
                    new ChatMessage("system", PromptTemplates.SystemPrompt(mode, targetLanguage)),
                    new ChatMessage("user", PromptTemplates.UserPrompt(text, mode, customPrompt))
                }
            };

            using var resp = await _http.PostAsJsonAsync("/api/chat", request, ct);
            if (!resp.IsSuccessStatusCode)
                return Result<string>.Fail($"Ollama returned {(int)resp.StatusCode}.");

            var body = await resp.Content.ReadFromJsonAsync<ChatResponse>(ct);
            var content = body?.Message?.Content?.Trim();
            return string.IsNullOrWhiteSpace(content)
                ? Result<string>.Fail("Empty LLM response.")
                : Result<string>.Ok(Sanitize(content!));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ollama processing failed.");
            return Result<string>.Fail(ex.Message);
        }
    }

    /// <summary>Strips wrapping quotes/backticks small models sometimes add despite instructions.</summary>
    private static string Sanitize(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();
        return s;
    }

    // ---- Ollama wire DTOs ----
    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatOptions
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("keep_alive")] public string KeepAlive { get; set; } = "5m";
        [JsonPropertyName("options")] public ChatOptions? Options { get; set; }
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private sealed class TagsResponse
    {
        [JsonPropertyName("models")] public List<TagModel>? Models { get; set; }
    }

    private sealed class TagModel
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
