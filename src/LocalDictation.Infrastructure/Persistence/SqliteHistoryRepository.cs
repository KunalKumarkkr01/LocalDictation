using Dapper;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LocalDictation.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed dictation history with FTS5 full-text search, favourites/pinning and
/// age-based retention pruning (design §10.2). Uses WAL for concurrent read performance.
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteHistoryRepository> _log;

    /// <summary>Creates the repository for the database at <paramref name="dbPath"/>.</summary>
    public SqliteHistoryRepository(string dbPath, ILogger<SqliteHistoryRepository> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        _log = log;
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        c.Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
        return c;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var c = Open();
        await c.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS history(
                id TEXT PRIMARY KEY,
                created_at TEXT NOT NULL,
                app TEXT,
                raw_text TEXT,
                processed_text TEXT,
                mode INTEGER,
                language TEXT,
                duration_ms INTEGER,
                favorite INTEGER DEFAULT 0,
                pinned INTEGER DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_history_created ON history(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_history_app ON history(app);

            CREATE VIRTUAL TABLE IF NOT EXISTS history_fts USING fts5(
                raw_text, processed_text, content='history', content_rowid='rowid');

            CREATE TRIGGER IF NOT EXISTS history_ai AFTER INSERT ON history BEGIN
                INSERT INTO history_fts(rowid, raw_text, processed_text)
                VALUES (new.rowid, new.raw_text, new.processed_text);
            END;
            CREATE TRIGGER IF NOT EXISTS history_ad AFTER DELETE ON history BEGIN
                INSERT INTO history_fts(history_fts, rowid, raw_text, processed_text)
                VALUES ('delete', old.rowid, old.raw_text, old.processed_text);
            END;
            """);
        _log.LogInformation("History database initialised.");
    }

    /// <inheritdoc />
    public async Task AddAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        await using var c = Open();
        await c.ExecuteAsync("""
            INSERT INTO history(id, created_at, app, raw_text, processed_text, mode, language, duration_ms, favorite, pinned)
            VALUES(@Id, @CreatedAt, @App, @RawText, @ProcessedText, @Mode, @Language, @DurationMs, @Favorite, @Pinned);
            """,
            new
            {
                Id = entry.Id.ToString(),
                CreatedAt = entry.CreatedAt.ToString("o"),
                entry.App,
                entry.RawText,
                entry.ProcessedText,
                Mode = (int)entry.Mode,
                entry.Language,
                entry.DurationMs,
                Favorite = entry.Favorite ? 1 : 0,
                Pinned = entry.Pinned ? 1 : 0
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken ct = default)
    {
        await using var c = Open();
        var where = new List<string>();
        var p = new DynamicParameters();

        string sql;
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            sql = "SELECT h.* FROM history h JOIN history_fts f ON f.rowid = h.rowid WHERE history_fts MATCH @Match";
            p.Add("Match", SanitizeFts(query.Text));
        }
        else
        {
            sql = "SELECT h.* FROM history h WHERE 1=1";
        }

        if (!string.IsNullOrWhiteSpace(query.App)) { where.Add("h.app = @App"); p.Add("App", query.App); }
        if (query.FavoritesOnly) where.Add("h.favorite = 1");
        if (where.Count > 0) sql += " AND " + string.Join(" AND ", where);
        sql += " ORDER BY h.created_at DESC LIMIT @Take OFFSET @Skip";
        // A default-constructed HistoryQuery struct has Take=0 (positional-record defaults are
        // bypassed by 'new()'); treat any non-positive page size as the standard page.
        p.Add("Take", query.Take <= 0 ? 200 : query.Take);
        p.Add("Skip", Math.Max(0, query.Skip));

        var rows = await c.QueryAsync<HistoryRow>(sql, p);
        return rows.Select(r => r.ToEntry()).ToList();
    }

    /// <inheritdoc />
    public async Task UpdateFlagsAsync(Guid id, bool favorite, bool pinned, CancellationToken ct = default)
    {
        await using var c = Open();
        await c.ExecuteAsync("UPDATE history SET favorite=@F, pinned=@P WHERE id=@Id",
            new { F = favorite ? 1 : 0, P = pinned ? 1 : 0, Id = id.ToString() });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = Open();
        await c.ExecuteAsync("DELETE FROM history WHERE id=@Id", new { Id = id.ToString() });
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(TimeSpan retention, CancellationToken ct = default)
    {
        await using var c = Open();
        var cutoff = DateTimeOffset.Now.Subtract(retention).ToString("o");
        return await c.ExecuteAsync("DELETE FROM history WHERE pinned=0 AND created_at < @Cutoff", new { Cutoff = cutoff });
    }

    /// <summary>Escapes an FTS query as a single quoted phrase to avoid syntax errors from user input.</summary>
    private static string SanitizeFts(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    // Row DTO mapping snake_case columns to CLR types.
    private sealed class HistoryRow
    {
        public string id { get; set; } = "";
        public string created_at { get; set; } = "";
        public string? app { get; set; }
        public string? raw_text { get; set; }
        public string? processed_text { get; set; }
        public long mode { get; set; }
        public string? language { get; set; }
        public long duration_ms { get; set; }
        public long favorite { get; set; }
        public long pinned { get; set; }

        public HistoryEntry ToEntry() => new()
        {
            Id = Guid.TryParse(id, out var g) ? g : Guid.NewGuid(),
            CreatedAt = DateTimeOffset.TryParse(created_at, out var d) ? d : DateTimeOffset.Now,
            App = app ?? "",
            RawText = raw_text ?? "",
            ProcessedText = processed_text ?? "",
            Mode = (ProcessingMode)mode,
            Language = language ?? "en",
            DurationMs = duration_ms,
            Favorite = favorite != 0,
            Pinned = pinned != 0
        };
    }
}
