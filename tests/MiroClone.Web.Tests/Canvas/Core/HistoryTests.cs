using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class HistoryTests
{
    private static readonly Guid[] Ids = Fixtures.Pool(6);

    [Fact]
    public void UndoRestoresAndRedoReappliesALocalAction()
    {
        var document = new CanvasDocument();
        var history = new History();
        var layer = Fixtures.Rectangle(Ids[0], 0, 0, 10, 10);

        var applied = history.Execute(document, [LayerMutation.Upsert(layer)]);
        Assert.Equal([LayerMutation.Upsert(layer)], applied);
        Assert.Equal(1, document.Count);

        var undone = history.Undo(document);
        Assert.Equal(0, document.Count);
        Assert.Equal([LayerMutation.Delete(Ids[0])], undone);
        Assert.True(history.CanRedo);

        history.Redo(document);
        Assert.Equal([Ids[0]], document.Order);
    }

    [Fact]
    public void AnActionThatChangesNothingAddsNoStep()
    {
        var document = CanvasDocument.Load([Fixtures.Rectangle(Ids[0], 0, 0, 1, 1)]);
        var history = new History();

        var applied = history.Execute(document, [LayerMutation.Delete(Ids[5])]);

        Assert.Empty(applied);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ABatchIsOneStepToUndo()
    {
        var document = CanvasDocument.Load([Fixtures.Rectangle(Ids[0], 0, 0, 1, 1), Fixtures.Rectangle(Ids[1], 0, 0, 1, 1)]);
        var history = new History();

        history.Execute(document,
        [
            LayerMutation.Upsert(Fixtures.Rectangle(Ids[0], 9, 9, 1, 1)),
            LayerMutation.Upsert(Fixtures.Rectangle(Ids[1], 9, 9, 1, 1)),
            LayerMutation.Delete(Ids[0]),
        ]);

        history.Undo(document);

        Assert.False(history.CanUndo);
        Assert.Equal([Ids[0], Ids[1]], document.Order);
        Assert.All(document.Ordered(), layer => Assert.Equal(0, layer.X));
    }

    [Fact]
    public void ANewActionClearsTheRedoStack()
    {
        var document = new CanvasDocument();
        var history = new History();
        history.Execute(document, [LayerMutation.Upsert(Fixtures.Rectangle(Ids[0], 0, 0, 1, 1))]);
        history.Undo(document);

        history.Execute(document, [LayerMutation.Upsert(Fixtures.Rectangle(Ids[1], 0, 0, 1, 1))]);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void TheHistoryDropsTheOldestStepWhenItIsFull()
    {
        var document = new CanvasDocument();
        var history = new History(capacity: 2);
        for (var i = 0; i < 3; i++)
        {
            history.Execute(document, [LayerMutation.Upsert(Fixtures.Rectangle(Ids[i], 0, 0, 1, 1))]);
        }

        history.Undo(document);
        history.Undo(document);

        Assert.False(history.CanUndo);
        Assert.Equal([Ids[0]], document.Order);
    }

    [Fact]
    public void UndoAndRedoOnAnEmptyHistoryDoNothing()
    {
        var document = new CanvasDocument();
        var history = new History();

        Assert.Empty(history.Undo(document));
        Assert.Empty(history.Redo(document));
    }

    [Fact]
    public void ACapacityBelowOneIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new History(0));
    }

    [Fact]
    public void ExecuteRejectsANullBatch()
    {
        Assert.Throws<ArgumentNullException>(() => new History().Execute(new CanvasDocument(), null!));
    }

    [Fact]
    public void UndoingEverythingThenRedoingEverythingReturnsToTheSameStates()
    {
        var random = new TestRandom(71);
        var document = new CanvasDocument();
        var initial = Fixtures.Copy(document);
        var history = new History();

        for (var step = 0; step < 60; step++)
        {
            var roll = random.Next(10);
            if (roll < 6)
            {
                var batch = Enumerable.Range(0, random.Next(1, 4)).Select(_ => Fixtures.RandomMutation(random, Ids)).ToArray();
                history.Execute(document, batch);
            }
            else if (roll < 8)
            {
                history.Undo(document);
            }
            else
            {
                history.Redo(document);
            }
        }

        var beforeUndoAll = Fixtures.Copy(document);
        while (history.CanUndo)
        {
            history.Undo(document);
        }

        Fixtures.AssertSameDocument(initial, document);

        while (history.CanRedo)
        {
            history.Redo(document);
        }

        Fixtures.AssertSameDocument(beforeUndoAll, document);
    }
}
