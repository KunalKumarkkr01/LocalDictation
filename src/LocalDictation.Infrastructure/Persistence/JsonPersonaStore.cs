using System.Text.Json;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using LocalDictation.Application.Processing;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Persistence;

/// <summary>
/// Loads/saves <see cref="PersonaSettings"/> as JSON beside <c>settings.json</c>. Writes atomically
/// (temp + rename); seeds factory defaults on first run or an unreadable file. The same file format
/// is used for Import/Export.
/// </summary>
public sealed class JsonPersonaStore : IPersonaStore
{
    private const int CurrentSchema = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly ILogger<JsonPersonaStore> _log;

    /// <summary>Creates the store rooted at <paramref name="personasPath"/>.</summary>
    public JsonPersonaStore(string personasPath, ILogger<JsonPersonaStore> log)
    {
        _path = personasPath;
        _log = log;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    /// <inheritdoc />
    public async Task<PersonaSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                var defaults = PersonaSeeds.CreateDefaults();
                await SaveAsync(defaults, ct).ConfigureAwait(false);
                return defaults;
            }

            var json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<PersonaSettings>(json, Options);
            if (settings is null || settings.Personas.Count == 0) return PersonaSeeds.CreateDefaults();
            return Migrate(settings);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load personas; using defaults.");
            return PersonaSeeds.CreateDefaults();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(PersonaSettings settings, CancellationToken ct = default)
    {
        settings.SchemaVersion = CurrentSchema;
        var json = JsonSerializer.Serialize(settings, Options);
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        File.Move(tmp, _path, overwrite: true);
    }

    private PersonaSettings Migrate(PersonaSettings settings)
    {
        if (settings.SchemaVersion < CurrentSchema)
        {
            _log.LogInformation("Migrating personas from schema {From} to {To}", settings.SchemaVersion, CurrentSchema);
            settings.SchemaVersion = CurrentSchema;
        }
        return settings;
    }
}
