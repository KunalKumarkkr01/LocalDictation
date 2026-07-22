using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using LocalDictation.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LocalDictation.UnitTests.Personas;

public class JsonPersonaStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"personas-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Load_first_run_writes_and_returns_seed_defaults()
    {
        var path = TempFile();
        try
        {
            var store = new JsonPersonaStore(path, NullLogger<JsonPersonaStore>.Instance);
            var loaded = await store.LoadAsync();
            Assert.True(File.Exists(path));
            Assert.Contains(loaded.Personas, p => p.Id == "general");
            Assert.Equal("general", loaded.DefaultPersonaId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Save_then_Load_round_trips_edits()
    {
        var path = TempFile();
        try
        {
            var store = new JsonPersonaStore(path, NullLogger<JsonPersonaStore>.Instance);
            var s = PersonaSeeds.CreateDefaults();
            s.FindById("general")!.SystemPrompt = "EDITED PROMPT";
            s.AutoApply = false;
            await store.SaveAsync(s);

            var reloaded = await store.LoadAsync();
            Assert.Equal("EDITED PROMPT", reloaded.FindById("general")!.SystemPrompt);
            Assert.False(reloaded.AutoApply);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_corrupt_file_reseeds_defaults()
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, "{ not json");
            var store = new JsonPersonaStore(path, NullLogger<JsonPersonaStore>.Instance);
            var loaded = await store.LoadAsync();
            Assert.Contains(loaded.Personas, p => p.Id == "general");
        }
        finally { File.Delete(path); }
    }
}
