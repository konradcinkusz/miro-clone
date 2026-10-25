using MiroClone.CanvasService.Canvas;
using Xunit;

namespace MiroClone.CanvasService.Tests.Canvas;

/// <summary>The aggregate's rules, checked without any host: the layer cap, replay safety, ordering and validation.</summary>
public sealed class BoardCanvasTests
{
    private static readonly Rgb Blue = new(37, 99, 235);

    [Fact]
    public void UpsertAddsTheLayerAndAdvancesTheVersion()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var layer = Rectangle();

        var status = board.Apply(new LayerMutation(MutationKind.Upsert, layer));

        var state = board.Snapshot();
        Assert.Equal(MutationStatus.Changed, status);
        Assert.Equal(1, state.Version);
        Assert.Equal([layer.Id], state.LayerIds);
        Assert.Equal(layer.Id, state.Layers[layer.Id].Id);
    }

    [Fact]
    public void ReplayingTheSameUpsertIsUnchangedAndDoesNotAdvanceTheVersion()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var layer = Rectangle();
        board.Apply(new LayerMutation(MutationKind.Upsert, layer));

        // A deserialised copy has equal content but a different points list instance: it is still a replay.
        var replay = Rectangle() with { Id = layer.Id };
        var status = board.Apply(new LayerMutation(MutationKind.Upsert, replay));

        Assert.Equal(MutationStatus.Unchanged, status);
        Assert.Equal(1, board.Version);
    }

    [Fact]
    public void UpsertOfAnExistingLayerReplacesItWholeLastWriterWins()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var id = Guid.NewGuid();
        board.Apply(new LayerMutation(MutationKind.Upsert, Rectangle() with { Id = id, X = 10 }));

        var status = board.Apply(new LayerMutation(MutationKind.Upsert, Rectangle() with { Id = id, X = 99 }));

        var state = board.Snapshot();
        Assert.Equal(MutationStatus.Changed, status);
        Assert.Equal(99, state.Layers[id].X);
        Assert.Single(state.LayerIds);
    }

    [Fact]
    public void TheHundredAndFirstLayerIsRefusedButExistingLayersStillEdit()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var ids = Enumerable.Range(0, LayerRules.MaxLayers).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            board.Apply(new LayerMutation(MutationKind.Upsert, Rectangle() with { Id = id }));
        }

        var refused = board.Apply(new LayerMutation(MutationKind.Upsert, Rectangle()));
        var edited = board.Apply(new LayerMutation(MutationKind.Upsert, Rectangle() with { Id = ids[0], X = 5 }));

        Assert.Equal(MutationStatus.LayerLimitReached, refused);
        Assert.Equal(MutationStatus.Changed, edited);
        Assert.Equal(LayerRules.MaxLayers, board.Snapshot().LayerIds.Count);
    }

    [Fact]
    public void DeletingAnAbsentLayerIsUnchangedSoADeleteReplayIsHarmless()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());

        var status = board.Apply(new LayerMutation(MutationKind.Delete, Id: Guid.NewGuid()));

        Assert.Equal(MutationStatus.Unchanged, status);
        Assert.Equal(0, board.Version);
    }

    [Fact]
    public void DeletingALayerRemovesItFromTheOrder()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var keep = Rectangle();
        var drop = Rectangle();
        board.Apply(new LayerMutation(MutationKind.Upsert, keep));
        board.Apply(new LayerMutation(MutationKind.Upsert, drop));

        var status = board.Apply(new LayerMutation(MutationKind.Delete, Id: drop.Id));

        var state = board.Snapshot();
        Assert.Equal(MutationStatus.Changed, status);
        Assert.Equal([keep.Id], state.LayerIds);
        Assert.False(state.Layers.ContainsKey(drop.Id));
    }

    [Fact]
    public void ReorderPutsListedLayersFirstAndKeepsUnlistedOnTop()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var a = Rectangle();
        var b = Rectangle();
        var c = Rectangle();
        foreach (var layer in new[] { a, b, c })
        {
            board.Apply(new LayerMutation(MutationKind.Upsert, layer));
        }

        var status = board.Apply(new LayerMutation(MutationKind.Reorder, Order: [c.Id]));

        Assert.Equal(MutationStatus.Changed, status);
        Assert.Equal([c.Id, a.Id, b.Id], board.Snapshot().LayerIds);
    }

    [Fact]
    public void ReorderIgnoresUnknownIdsSoARaceWithADeleteStillConverges()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var a = Rectangle();
        var b = Rectangle();
        board.Apply(new LayerMutation(MutationKind.Upsert, a));
        board.Apply(new LayerMutation(MutationKind.Upsert, b));

        var status = board.Apply(new LayerMutation(MutationKind.Reorder, Order: [Guid.NewGuid(), b.Id, a.Id]));

        Assert.Equal(MutationStatus.Changed, status);
        Assert.Equal([b.Id, a.Id], board.Snapshot().LayerIds);
    }

    [Fact]
    public void ReorderWithDuplicatesIsInvalid()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var a = Rectangle();
        board.Apply(new LayerMutation(MutationKind.Upsert, a));

        var status = board.Apply(new LayerMutation(MutationKind.Reorder, Order: [a.Id, a.Id]));

        Assert.Equal(MutationStatus.Invalid, status);
    }

    [Fact]
    public void MalformedLayersAreRefusedWithoutChangingTheBoard()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var invalid = new[]
        {
            Rectangle() with { X = double.NaN },
            Rectangle() with { Width = -1 },
            Rectangle() with { Value = "not allowed on a rectangle" },
            Rectangle() with { Points = [new PathPoint(0, 0)] },
            new Layer(Guid.NewGuid(), LayerType.Path, 0, 0, 0, 0, Blue, Points: []),
            new Layer(Guid.Empty, LayerType.Rectangle, 0, 0, 1, 1, Blue),
            new Layer(Guid.NewGuid(), LayerType.Text, 0, 0, 1, 1, Blue, Value: new string('x', LayerRules.MaxValueLength + 1)),
        };

        foreach (var layer in invalid)
        {
            Assert.Equal(MutationStatus.Invalid, board.Apply(new LayerMutation(MutationKind.Upsert, layer)));
        }

        Assert.Equal(0, board.Version);
        Assert.Empty(board.Snapshot().LayerIds);
    }

    [Fact]
    public void AnUpsertWithoutALayerIsInvalid()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());

        Assert.Equal(MutationStatus.Invalid, board.Apply(new LayerMutation(MutationKind.Upsert)));
    }

    [Fact]
    public void RestoreKeepsTheStoredOrderAndVersion()
    {
        var boardId = Guid.NewGuid();
        var top = Rectangle();
        var bottom = Rectangle();
        var state = new CanvasState(
            Version: 7,
            LayerIds: [bottom.Id, top.Id],
            Layers: new Dictionary<Guid, Layer> { [top.Id] = top, [bottom.Id] = bottom });

        var restored = BoardCanvas.Restore(boardId, state);

        Assert.Equal(7, restored.Version);
        Assert.Equal([bottom.Id, top.Id], restored.Snapshot().LayerIds);
    }

    [Fact]
    public void RestoreRefusesAStoredBoardAboveTheLayerCap()
    {
        var layers = Enumerable.Range(0, LayerRules.MaxLayers + 1).Select(_ => Rectangle()).ToList();
        var state = new CanvasState(1, layers.Select(layer => layer.Id).ToList(), layers.ToDictionary(layer => layer.Id));

        Assert.Throws<InvalidOperationException>(() => BoardCanvas.Restore(Guid.NewGuid(), state));
    }

    private static Layer Rectangle() => new(Guid.NewGuid(), LayerType.Rectangle, 0, 0, 10, 10, Blue);
}
