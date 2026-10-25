using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MiroClone.CanvasService.Persistence;

/// <summary>One board's stored snapshot: the JSON document and the promoted columns the store needs to order and bound it.</summary>
public sealed record StoredSnapshot(Guid BoardId, long Version, DateTimeOffset UpdatedAt, string Json);

/// <summary>
/// Durable home of board snapshots. Kept behind an interface so the registry can be tested against a fault-injecting fake.
/// </summary>
public interface ICanvasSnapshotStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    /// <summary>The most recently updated snapshots, newest first, at most <paramref name="limit"/> of them.</summary>
    Task<IReadOnlyList<StoredSnapshot>> LoadRecentAsync(int limit, CancellationToken cancellationToken);

    Task<StoredSnapshot?> FindAsync(Guid boardId, CancellationToken cancellationToken);

    /// <summary>Idempotent: writing the same board twice leaves one row at the latest version (state-snapshot §2).</summary>
    Task UpsertAsync(StoredSnapshot snapshot, CancellationToken cancellationToken);

    Task DeleteAsync(Guid boardId, CancellationToken cancellationToken);
}

/// <summary>
/// The "canvas" database holds one table of JSON snapshots, not an entity model. That is the write-behind shape of
/// state-snapshot-persistence, and P4's migrated-schema rule does not apply to it (ADR 0011). The DDL is idempotent
/// and runs at startup; the statements are plain SQL that both PostgreSQL and SQLite accept.
/// </summary>
internal sealed class CanvasSnapshotStore(IServiceScopeFactory scopes) : ICanvasSnapshotStore
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS canvas_snapshots (
            board_id uuid NOT NULL PRIMARY KEY,
            version bigint NOT NULL,
            updated_at_ms bigint NOT NULL,
            snapshot text NOT NULL
        )
        """;

    private const string CreateIndexSql =
        "CREATE INDEX IF NOT EXISTS canvas_snapshots_updated_idx ON canvas_snapshots (updated_at_ms)";

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CanvasDbContext>().Database;
        await database.ExecuteSqlRawAsync(CreateTableSql, cancellationToken);
        await database.ExecuteSqlRawAsync(CreateIndexSql, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSnapshot>> LoadRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CanvasDbContext>().Database;
        var rows = await database.SqlQuery<SnapshotRow>($"""
            SELECT CAST(board_id AS text) AS "BoardId", version AS "Version", updated_at_ms AS "UpdatedAtMs", snapshot AS "Snapshot"
            FROM canvas_snapshots
            ORDER BY updated_at_ms DESC
            LIMIT {limit}
            """).ToListAsync(cancellationToken);

        return rows.Select(ToStored).ToList();
    }

    public async Task<StoredSnapshot?> FindAsync(Guid boardId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CanvasDbContext>().Database;
        var rows = await database.SqlQuery<SnapshotRow>($"""
            SELECT CAST(board_id AS text) AS "BoardId", version AS "Version", updated_at_ms AS "UpdatedAtMs", snapshot AS "Snapshot"
            FROM canvas_snapshots
            WHERE board_id = {boardId}
            """).ToListAsync(cancellationToken);

        return rows.Count == 0 ? null : ToStored(rows[0]);
    }

    public async Task UpsertAsync(StoredSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CanvasDbContext>().Database;
        var updatedAtMs = snapshot.UpdatedAt.ToUnixTimeMilliseconds();
        await database.ExecuteSqlAsync($"""
            INSERT INTO canvas_snapshots (board_id, version, updated_at_ms, snapshot)
            VALUES ({snapshot.BoardId}, {snapshot.Version}, {updatedAtMs}, {snapshot.Json})
            ON CONFLICT (board_id) DO UPDATE SET
                version = excluded.version,
                updated_at_ms = excluded.updated_at_ms,
                snapshot = excluded.snapshot
            """, cancellationToken);
    }

    public async Task DeleteAsync(Guid boardId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CanvasDbContext>().Database;
        await database.ExecuteSqlAsync($"DELETE FROM canvas_snapshots WHERE board_id = {boardId}", cancellationToken);
    }

    private static StoredSnapshot ToStored(SnapshotRow row) => new(
        Guid.Parse(row.BoardId),
        row.Version,
        DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtMs),
        row.Snapshot);

    /// <summary>Column names are aliased to these property names in the queries above.</summary>
    private sealed class SnapshotRow
    {
        public string BoardId { get; set; } = string.Empty;

        public long Version { get; set; }

        public long UpdatedAtMs { get; set; }

        public string Snapshot { get; set; } = string.Empty;
    }
}
