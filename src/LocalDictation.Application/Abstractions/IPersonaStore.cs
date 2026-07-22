using LocalDictation.Application.Configuration;

namespace LocalDictation.Application.Abstractions;

/// <summary>Loads and saves persona configuration (<c>personas.json</c>).</summary>
public interface IPersonaStore
{
    /// <summary>Loads personas, seeding defaults on first run or an unreadable file.</summary>
    Task<PersonaSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Atomically persists persona configuration.</summary>
    Task SaveAsync(PersonaSettings settings, CancellationToken ct = default);
}
