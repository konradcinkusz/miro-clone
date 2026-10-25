using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class LayerOrderingTests
{
    private static readonly Guid[] Ids = Fixtures.Pool(5);

    [Fact]
    public void BringToFrontMovesTheSelectionToTheTopKeepingItsOrder()
    {
        var order = new[] { Ids[0], Ids[1], Ids[2], Ids[3] };

        var result = LayerOrdering.BringToFront(order, [Ids[1], Ids[3]]);

        Assert.Equal([Ids[0], Ids[2], Ids[1], Ids[3]], result);
    }

    [Fact]
    public void SendToBackMovesTheSelectionToTheBottomKeepingItsOrder()
    {
        var order = new[] { Ids[0], Ids[1], Ids[2], Ids[3] };

        var result = LayerOrdering.SendToBack(order, [Ids[2], Ids[3]]);

        Assert.Equal([Ids[2], Ids[3], Ids[0], Ids[1]], result);
    }

    [Fact]
    public void IdsNotInTheOrderAreIgnored()
    {
        var order = new[] { Ids[0], Ids[1] };

        Assert.Equal([Ids[1], Ids[0]], LayerOrdering.BringToFront(order, [Ids[0], Ids[4]]));
        Assert.Equal([Ids[0], Ids[1]], LayerOrdering.SendToBack(order, [Ids[4]]));
    }

    [Fact]
    public void OrderingIsIdempotent()
    {
        var order = new[] { Ids[0], Ids[1], Ids[2] };

        var front = LayerOrdering.BringToFront(order, [Ids[2]]);

        Assert.Equal(front, LayerOrdering.BringToFront(front, [Ids[2]]));
        Assert.Equal(order, LayerOrdering.SendToBack(order, [Ids[0]]));
    }
}
