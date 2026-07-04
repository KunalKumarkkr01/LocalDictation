using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using LocalDictation.Domain;
using LocalDictation.Infrastructure.Persistence;
using LocalDictation.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalDictation.UnitTests;

/// <summary>Tests for shared primitives and prompt building.</summary>
public class CoreTests
{
    [Fact]
    public void Result_ok_carries_value()
    {
        var r = Result<string>.Ok("x");
        Assert.True(r.IsSuccess);
        Assert.Equal("x", r.Value);
    }

    [Fact]
    public void Result_fail_falls_back()
    {
        var r = Result<int>.Fail("boom");
        Assert.False(r.IsSuccess);
        Assert.Equal(42, r.ValueOr(42));
    }

    [Fact]
    public void Grammar_prompt_constrains_output()
    {
        var system = PromptTemplates.SystemPrompt(ProcessingMode.GrammarCorrection, "en");
        Assert.Contains("ONLY", system);
    }

    [Fact]
    public void Custom_prompt_substitutes_text_token()
    {
        var user = PromptTemplates.UserPrompt("hello", ProcessingMode.Custom, "Uppercase this: {text}");
        Assert.Equal("Uppercase this: hello", user);
    }

    [Fact]
    public void Transcript_final_text_prefers_processed()
    {
        var t = new Transcript { RawText = "raw", ProcessedText = "clean" };
        Assert.Equal("clean", t.FinalText);
    }
}

/// <summary>Round-trip and query tests for the persistence adapters (real temp files).</summary>
public class PersistenceTests
{
    [Fact]
    public async Task Settings_round_trip_preserves_values()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ld-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
            var settings = await store.LoadAsync();
            settings.Hotkey = "Alt+Shift+D";
            settings.WhisperModel = SpeechModelSize.Small;
            await store.SaveAsync(settings);

            var reloaded = await new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance).LoadAsync();
            Assert.Equal("Alt+Shift+D", reloaded.Hotkey);
            Assert.Equal(SpeechModelSize.Small, reloaded.WhisperModel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task History_add_and_fulltext_search()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ld-hist-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteHistoryRepository(path, NullLogger<SqliteHistoryRepository>.Instance);
            await repo.InitializeAsync();
            await repo.AddAsync(new HistoryEntry { App = "chrome", RawText = "quarterly finance report", ProcessedText = "Quarterly finance report." });
            await repo.AddAsync(new HistoryEntry { App = "teams", RawText = "hello team", ProcessedText = "Hello team." });

            var all = await repo.QueryAsync(new HistoryQuery());
            Assert.Equal(2, all.Count);

            var hit = await repo.QueryAsync(new HistoryQuery("finance"));
            Assert.Single(hit);
            Assert.Equal("chrome", hit[0].App);
        }
        finally { SafeDelete(path); }
    }

    [Fact]
    public async Task History_prune_respects_pinned()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ld-prune-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteHistoryRepository(path, NullLogger<SqliteHistoryRepository>.Instance);
            await repo.InitializeAsync();
            var old = new HistoryEntry { App = "a", RawText = "old", ProcessedText = "old", CreatedAt = DateTimeOffset.Now.AddDays(-100), Pinned = false };
            var pinned = new HistoryEntry { App = "b", RawText = "keep", ProcessedText = "keep", CreatedAt = DateTimeOffset.Now.AddDays(-100), Pinned = true };
            await repo.AddAsync(old);
            await repo.AddAsync(pinned);

            var removed = await repo.PruneAsync(TimeSpan.FromDays(30));
            Assert.Equal(1, removed);
            var remaining = await repo.QueryAsync(new HistoryQuery());
            Assert.Single(remaining);
            Assert.True(remaining[0].Pinned);
        }
        finally { SafeDelete(path); }
    }

    private static void SafeDelete(string path)
    {
        foreach (var f in new[] { path, path + "-shm", path + "-wal" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* ignore */ }
    }
}
