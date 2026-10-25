using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using Xunit;

namespace MiroClone.CanvasService.Tests.Persistence;

/// <summary>
/// The write-behind rules (state-snapshot-persistence §1, §4, §5): dirty boards flush, a failed write is retried, a failed
/// delete is not forgotten, and a dead store never turns into an empty board.
/// </summary>
public sealed class CanvasRegistryTests
{
    private readonly FakeSnapshotStore _store = new();

    [Fact]
    public async Task FlushWritesEveryDirtyBoardOnceAndClearsTheDirtySet()
    {
        var registry = NewRegistry();
        var boardId = Guid.NewGuid();
        var canvas = await registry.GetOrLoadAsync(boardId, TestContext.Current.CancellationToken);
        canvas.Apply(Upsert(Guid.NewGuid()));
        registry.MarkDirty(boardId);

        await registry.FlushAsync(TestContext.Current.CancellationToken);
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        var row = _store.Row(boardId);
        Assert.NotNull(row);
        Assert.Equal(1, row.Version);
        Assert.Equal(1, _store.RowCount);
    }

    [Fact]
    public async Task AFailedWriteRequeuesTheBoardAndTheNextTickWritesIt()
    {
        var registry = NewRegistry();
        var boardId = Guid.NewGuid();
        var canvas = await registry.GetOrLoadAsync(boardId, TestContext.Current.CancellationToken);
        canvas.Apply(Upsert(Guid.NewGuid()));
        registry.MarkDirty(boardId);
        _store.Offline = true;

        await registry.FlushAsync(TestContext.Current.CancellationToken);

        Assert.False(registry.IsPersisting);
        Assert.Null(_store.Row(boardId));

        _store.Offline = false;
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(registry.IsPersisting);
        Assert.NotNull(_store.Row(boardId));
    }

    [Fact]
    public async Task ARemovedBoardIsDeletedFromTheStoreAndDoesNotComeBackOnRehydrate()
    {
        var registry = NewRegistry();
        var boardId = Guid.NewGuid();
        var canvas = await registry.GetOrLoadAsync(boardId, TestContext.Current.CancellationToken);
        canvas.Apply(Upsert(Guid.NewGuid()));
        registry.MarkDirty(boardId);
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        await registry.RemoveAsync(boardId, TestContext.Current.CancellationToken);

        Assert.Null(_store.Row(boardId));
        var restarted = NewRegistry();
        await restarted.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(restarted.TryGet(boardId));
    }

    [Fact]
    public async Task AFailedDeleteStaysPendingUntilTheStoreRecovers()
    {
        var registry = NewRegistry();
        var boardId = Guid.NewGuid();
        var canvas = await registry.GetOrLoadAsync(boardId, TestContext.Current.CancellationToken);
        canvas.Apply(Upsert(Guid.NewGuid()));
        registry.MarkDirty(boardId);
        await registry.FlushAsync(TestContext.Current.CancellationToken);
        _store.Offline = true;

        await registry.RemoveAsync(boardId, TestContext.Current.CancellationToken);
        Assert.False(registry.IsPersisting);
        _store.Offline = false;
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Null(_store.Row(boardId));
        Assert.True(registry.IsPersisting);
    }

    [Fact]
    public async Task ALoadThatFailsIsReportedAsUnavailableAndNeverFlushesAnEmptyBoard()
    {
        var registry = NewRegistry();
        var boardId = Guid.NewGuid();
        await _store.UpsertAsync(BoardWithOneLayer(boardId), TestContext.Current.CancellationToken);
        _store.Offline = true;

        await Assert.ThrowsAsync<CanvasStoreUnavailableException>(
            () => registry.GetOrLoadAsync(boardId, TestContext.Current.CancellationToken));
        Assert.Null(registry.TryGet(boardId));

        _store.Offline = false;
        var loaded = await registry.GetOrLoadAsync(boardId, TestContext.Current.CancellationToken);
        Assert.Single(loaded.Snapshot().LayerIds);
    }

    [Fact]
    public async Task InitializeRestoresTheMostRecentBoardsIntoMemory()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _store.UpsertAsync(BoardWithOneLayer(first), TestContext.Current.CancellationToken);
        await _store.UpsertAsync(BoardWithOneLayer(second), TestContext.Current.CancellationToken);
        var registry = NewRegistry(rehydrateLimit: 1);

        await registry.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, registry.LoadedBoards);
        Assert.Single(registry.TryGet(second)?.Snapshot().LayerIds ?? []);
    }

    [Fact]
    public async Task AFailedStartupIsRecordedAndTheServiceRunsFromMemory()
    {
        _store.Offline = true;
        var registry = NewRegistry();

        await registry.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(registry.IsPersisting);
        var board = Guid.NewGuid();
        _store.Offline = false;
        var canvas = await registry.GetOrLoadAsync(board, TestContext.Current.CancellationToken);
        Assert.Equal(MutationStatus.Changed, canvas.Apply(Upsert(Guid.NewGuid())));
    }

    private CanvasRegistry NewRegistry(int rehydrateLimit = 1000) => new(
        _store,
        Options.Create(new CanvasOptions { RehydrateLimit = rehydrateLimit }),
        NullLogger<CanvasRegistry>.Instance);

    private static LayerMutation Upsert(Guid id) => new(
        MutationKind.Upsert,
        new Layer(id, LayerType.Rectangle, 0, 0, 10, 10, new Rgb(0, 0, 0)));

    private static StoredSnapshot BoardWithOneLayer(Guid boardId)
    {
        var canvas = BoardCanvas.Empty(boardId);
        canvas.Apply(Upsert(Guid.NewGuid()));
        var json = JsonSerializer.Serialize(canvas.Snapshot(), CanvasJson.Options);
        return new StoredSnapshot(boardId, canvas.Version, DateTimeOffset.UtcNow, json);
    }
}
