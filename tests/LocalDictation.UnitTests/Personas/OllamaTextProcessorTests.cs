using System.Net;
using System.Text;
using System.Text.Json;
using LocalDictation.Application.Configuration;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class OllamaTextProcessorTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            var json = "{\"message\":{\"role\":\"assistant\",\"content\":\"clean text\"}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Override_is_used_as_system_message_and_num_ctx_is_sent()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var settings = new AppSettings { LlmContextTokens = 8192 };
        var proc = new OllamaTextProcessor(http, settings, NullLogger<OllamaTextProcessor>.Instance);

        var result = await proc.ProcessAsync("raw words", ProcessingMode.Custom,
            systemPromptOverride: "PERSONA SYSTEM PROMPT");

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(handler.Body!);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal("PERSONA SYSTEM PROMPT", messages[0].GetProperty("content").GetString());
        Assert.Equal("raw words", messages[1].GetProperty("content").GetString());
        Assert.Equal(8192, doc.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }
}
