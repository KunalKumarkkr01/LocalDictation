using System.Text.Json;
using LocalDictation.Application.Abstractions;
using LocalDictation.Application.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Persistence;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON under <c>%AppData%/LocalDictation</c>.
/// Writes atomically (temp + rename) and applies forward migrations by schema version.
/// </summary>
/// <remarks>
/// Secrets (e.g. future cloud plugin keys) are DPAPI-encrypted before serialisation; the
/// core app ships none, so the file is plain settings only. Round-trips unknown properties
/// so newer-version settings survive a downgrade.
/// </remarks>
public sealed class JsonSettingsStore : ISettingsStore
{
    private const int CurrentSchema = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly ILogger<JsonSettingsStore> _log;

    /// <summary>Creates the store rooted at <paramref name="settingsPath"/>.</summary>
    public JsonSettingsStore(string settingsPath, ILogger<JsonSettingsStore> log)
    {
        _path = settingsPath;
        _log = log;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                var defaults = new AppSettings();
                await SaveAsync(defaults, ct);
                return defaults;
            }

            var json = await File.ReadAllTextAsync(_path, ct);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            return Migrate(settings);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load settings; using defaults.");
            return new AppSettings();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        settings.SchemaVersion = CurrentSchema;
        var json = JsonSerializer.Serialize(settings, Options);
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, _path, overwrite: true); // atomic replace
    }

    /// <summary>Applies ordered migrations to bring an older file up to the current schema.</summary>
    private AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion < CurrentSchema)
        {
            _log.LogInformation("Migrating settings from schema {From} to {To}", settings.SchemaVersion, CurrentSchema);
            // Future migration steps go here, e.g.:
            // if (settings.SchemaVersion < 2) { ...; settings.SchemaVersion = 2; }
            settings.SchemaVersion = CurrentSchema;
        }
        return settings;
    }
}
