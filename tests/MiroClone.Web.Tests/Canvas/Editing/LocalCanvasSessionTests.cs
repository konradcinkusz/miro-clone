using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Editing;

public sealed class LocalCanvasSessionTests
{
    [Fact]
    public void CommitAppliesTheMutationsAndReturnsTheOnesThatChangedTheBoard()
    {
        var session = new LocalCanvasSession(CanvasDocument.Load([]));
        var layer = Layer();

        var applied = session.Commit([LayerMutation.Upsert(layer)]);

        Assert.Single(applied);
        Assert.True(session.Document.Contains(layer.Id));
    }

    [Fact]
    public void CommitOfAChangeThatChangesNothingReturnsNothing()
    {
        var layer = Layer();
        var session = new LocalCanvasSession(CanvasDocument.Load([layer]));

        var applied = session.Commit([LayerMutation.Upsert(layer)]);

        Assert.Empty(applied);
    }

    [Fact]
    public async Task DisposingIsSafeAndLeavesTheDocumentReadable()
    {
        var session = new LocalCanvasSession(CanvasDocument.Load([Layer()]));

        await session.DisposeAsync();

        Assert.Equal(1, session.Document.Count);
    }

    private static Layer Layer() => new(Guid.NewGuid(), LayerType.Rectangle, 0, 0, 10, 10, new Rgb(1, 1, 1));
}
