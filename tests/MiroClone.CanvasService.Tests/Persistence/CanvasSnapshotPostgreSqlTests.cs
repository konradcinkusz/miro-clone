using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using MiroClone.ServiceDefaults;
using Xunit;

namespace MiroClone.CanvasService.Tests.Persistence;

/// <summary>
/// The snapshot store's SQL on PostgreSQL: the DDL, the upsert, the parameterised reads and the delete. SQLite cannot prove
/// these, so this runs only when TEST_POSTGRES holds a server connection string; otherwise it is skipped.
/// </summary>
public sealed class CanvasSnapshotPostgreSqlTests
{
    [Fact]
    public async Task ASnapshotWrittenThroughTheRegistryIsReadBackOnPostgreSql()
    {
        var serverConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(serverConnection))
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL snapshot test is skipped.");
        }

        var databaseConnection = await PostgreSqlTestDatabase.PrepareAsync(
            serverConnection,
            TestContext.Current.CancellationToken,
            databaseName: "canvas_snapshot_test");
        await DropSnapshotTableAsync(databaseConnection, TestContext.Current.CancellationToken);
        await using var app = await ServiceHost.StartAsync(new Dictionary<string, string?>
        {
            ["Database:Provider"] = DatabaseProviders.Npgsql,
            ["ConnectionStrings:canvas"] = databaseConnection,
        }, realSnapshotStore: true);
        var registry = app.Services.GetRequiredService<CanvasRegistry>();
        var store = app.Services.GetRequiredService<ICanvasSnapshotStore>();
        var boardId = Guid.NewGuid();
        var layer = new Layer(Guid.NewGuid(), LayerType.Path, 1, 2, 30, 40, new Rgb(9, 8, 7), Points: [new PathPoint(0, 0), new PathPoint(3, 4)]);

        var canvas = await registry.GetOrLoadAsync(boardId, TestTokens.Workspace, TestContext.Current.CancellationToken);
        canvas.Apply(new LayerMutation(MutationKind.Upsert, layer));
        registry.MarkDirty(boardId);
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        var stored = await store.FindAsync(boardId, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Version);
        Assert.Contains(boardId, (await store.LoadRecentAsync(100, TestContext.Current.CancellationToken)).Select(row => row.BoardId));
        Assert.True(registry.IsPersisting);

        await registry.RemoveAsync(boardId, TestContext.Current.CancellationToken);

        Assert.Null(await store.FindAsync(boardId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// CREATE TABLE IF NOT EXISTS never alters a table that is already there, so a database left by an earlier build keeps its old
    /// columns. The test starts from an empty table so it measures the current DDL (ADR 0015 records the evolution rule).
    /// </summary>
    private static async Task DropSnapshotTableAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var drop = new NpgsqlCommand("DROP TABLE IF EXISTS canvas_snapshots", connection);
        await drop.ExecuteNonQueryAsync(cancellationToken);
    }
}
