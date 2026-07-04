using LocalDictation.Domain;
using LocalDictation.Application.Configuration;

namespace LocalDictation.Application.Abstractions;

/// <summary>Query parameters for searching/filtering history.</summary>
/// <param name="Text">Full-text search term (empty = all).</param>
/// <param name="App">Filter by app process name (null = any).</param>
/// <param name="FavoritesOnly">Restrict to favourites.</param>
/// <param name="Skip">Paging offset.</param>
/// <param name="Take">Page size.</param>
public readonly record struct HistoryQuery(string Text = "", string? App = null, bool FavoritesOnly = false, int Skip = 0, int Take = 50);

/// <summary>Persists and queries dictation history (SQLite + FTS5 backed).</summary>
public interface IHistoryRepository
{
    /// <summary>Ensures the schema exists.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Inserts a completed dictation.</summary>
    Task AddAsync(HistoryEntry entry, CancellationToken ct = default);

    /// <summary>Searches/filters history, newest first.</summary>
    Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken ct = default);

    /// <summary>Toggles favourite / pinned flags.</summary>
    Task UpdateFlagsAsync(Guid id, bool favorite, bool pinned, CancellationToken ct = default);

    /// <summary>Deletes an entry.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Prunes entries older than the retention window (pinned are kept).</summary>
    Task<int> PruneAsync(TimeSpan retention, CancellationToken ct = default);
}

/// <summary>Loads and saves strongly-typed application settings.</summary>
public interface ISettingsStore
{
    /// <summary>Loads settings (returns defaults on first run), applying migrations.</summary>
    Task<AppSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Atomically persists settings to disk.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
