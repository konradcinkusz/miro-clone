using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using Xunit;

namespace MiroClone.CanvasService.Tests.Persistence;

/// <summary>The real snapshot store on SQLite: the idempotent DDL, the upsert, the bounded load and the delete.</summary>
public sealed class CanvasSnapshotStoreTests : IAsyncLifetime
{
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"canvas-store-{Guid.NewGuid():N}.db");
    private WebApplication _app = null!;
    private ICanvasSnapshotStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await ServiceHost.StartAsync(ServiceHost.SqliteFile(_databaseFile), realSnapshotStore: true);
        _store = _app.Services.GetRequiredService<ICanvasSnapshotStore>();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databaseFile);
    }

    [Fact]
    public async Task AnUpsertedSnapshotComesBackAsTheSameDocument()
    {
        var boardId = Guid.NewGuid();
        var snapshot = Stored(boardId, version: 3, layers: 2);

        await _store.UpsertAsync(snapshot, TestContext.Current.CancellationToken);
        var found = await _store.FindAsync(boardId, TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(snapshot.WorkspaceId, found.WorkspaceId);
        Assert.Equal(snapshot.Version, found.Version);
        Assert.Equal(snapshot.Json, found.Json);
        Assert.Equal(snapshot.UpdatedAt.ToUnixTimeMilliseconds(), found.UpdatedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task UpsertingTheSameBoardTwiceLeavesOneRowAtTheLatestVersion()
    {
        var boardId = Guid.NewGuid();

        await _store.UpsertAsync(Stored(boardId, version: 1, layers: 1), TestContext.Current.CancellationToken);
        await _store.UpsertAsync(Stored(boardId, version: 2, layers: 2), TestContext.Current.CancellationToken);

        var recent = await _store.LoadRecentAsync(10, TestContext.Current.CancellationToken);
        Assert.Single(recent, row => row.BoardId == boardId);
        Assert.Equal(2, (await _store.FindAsync(boardId, TestContext.Current.CancellationToken))!.Version);
    }

    [Fact]
    public async Task LoadRecentIsNewestFirstAndBoundedByTheLimit()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await _store.UpsertAsync(Stored(older, version: 1, layers: 1, at: DateTimeOffset.UtcNow.AddMinutes(-5)), TestContext.Current.CancellationToken);
        await _store.UpsertAsync(Stored(newer, version: 1, layers: 1, at: DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        var limited = await _store.LoadRecentAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal([newer], limited.Select(row => row.BoardId));
    }

    [Fact]
    public async Task DeleteRemovesTheRow()
    {
        var boardId = Guid.NewGuid();
        await _store.UpsertAsync(Stored(boardId, version: 1, layers: 1), TestContext.Current.CancellationToken);

        await _store.DeleteAsync(boardId, TestContext.Current.CancellationToken);

        Assert.Null(await _store.FindAsync(boardId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheSchemaIsCreatedAgainOnEveryStartWithoutError()
    {
        await _store.EnsureSchemaAsync(TestContext.Current.CancellationToken);
        await _store.EnsureSchemaAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await _store.LoadRecentAsync(10, TestContext.Current.CancellationToken));
    }

    private static StoredSnapshot Stored(Guid boardId, long version, int layers, DateTimeOffset? at = null)
    {
        var canvas = BoardCanvas.Empty(boardId, TestTokens.Workspace);
        for (var index = 0; index < layers; index++)
        {
            canvas.Apply(new LayerMutation(
                MutationKind.Upsert,
                new Layer(Guid.NewGuid(), LayerType.Ellipse, index, index, 4, 4, new Rgb(1, 2, 3))));
        }

        var json = JsonSerializer.Serialize(canvas.Snapshot() with { Version = version }, CanvasJson.Options);
        return new StoredSnapshot(boardId, TestTokens.Workspace, version, at ?? DateTimeOffset.UtcNow, json);
    }
}
