using LocalDictation.Domain;

namespace LocalDictation.Application.Configuration;

/// <summary>
/// Persisted persona configuration (<c>personas.json</c>), a sibling of <see cref="AppSettings"/>.
/// Kept separate because the file doubles as the import/export format.
/// </summary>
public sealed class PersonaSettings
{
    /// <summary>Schema version for forward-compatible migration.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Master switch for auto-detection. When off, personas apply only via the picker.</summary>
    public bool AutoApply { get; set; } = true;

    /// <summary>Id of the fallback persona used when no app matches (and it is enabled).</summary>
    public string? DefaultPersonaId { get; set; } = "general";

    /// <summary>Second global hotkey that opens the persona picker.</summary>
    public string PickerHotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>All personas — System, BuiltIn and User.</summary>
    public List<Persona> Personas { get; set; } = new();

    /// <summary>Finds a persona by id (case-insensitive); null id/miss returns null.</summary>
    public Persona? FindById(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : Personas.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
