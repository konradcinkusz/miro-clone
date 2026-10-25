using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class ResizerTests
{
    private static readonly Bounds Box = new(0, 0, 100, 50);

    [Fact]
    public void HandlesSitOnTheCornersAndEdgeMidpoints()
    {
        Assert.Equal(new Point(0, 0), Resizer.HandlePosition(Box, ResizeHandle.NorthWest));
        Assert.Equal(new Point(50, 0), Resizer.HandlePosition(Box, ResizeHandle.North));
        Assert.Equal(new Point(100, 50), Resizer.HandlePosition(Box, ResizeHandle.SouthEast));
        Assert.Equal(new Point(0, 25), Resizer.HandlePosition(Box, ResizeHandle.West));
    }

    [Fact]
    public void AHandleIsFoundWithinItsSquareAndCornersTakePrecedence()
    {
        Assert.Equal(ResizeHandle.NorthWest, Resizer.HandleAt(Box, new Point(2, 2), 3));
        Assert.Equal(ResizeHandle.East, Resizer.HandleAt(Box, new Point(102, 25), 3));
        Assert.Null(Resizer.HandleAt(Box, new Point(50, 25), 3));
    }

    [Fact]
    public void DraggingAnEdgeMovesOnlyThatEdge()
    {
        Assert.Equal(new Bounds(0, 0, 150, 50), Resizer.ResizeBounds(Box, ResizeHandle.East, new Point(150, 999)));
        Assert.Equal(new Bounds(20, 0, 80, 50), Resizer.ResizeBounds(Box, ResizeHandle.West, new Point(20, -999)));
        Assert.Equal(new Bounds(0, 10, 100, 40), Resizer.ResizeBounds(Box, ResizeHandle.North, new Point(-999, 10)));
        Assert.Equal(new Bounds(0, 0, 100, 80), Resizer.ResizeBounds(Box, ResizeHandle.South, new Point(999, 80)));
    }

    [Fact]
    public void ACornerMovesBothEdges()
    {
        Assert.Equal(new Bounds(0, 0, 120, 80), Resizer.ResizeBounds(Box, ResizeHandle.SouthEast, new Point(120, 80)));
    }

    [Fact]
    public void DraggingPastTheAnchorFlipsTheBoxInsteadOfInvertingIt()
    {
        Assert.Equal(new Bounds(-30, 0, 30, 50), Resizer.ResizeBounds(Box, ResizeHandle.East, new Point(-30, 0)));
        Assert.Equal(new Bounds(100, 0, 50, 50), Resizer.ResizeBounds(Box, ResizeHandle.West, new Point(150, 0)));
    }

    [Fact]
    public void ASizeNeverGoesBelowTheMinimum()
    {
        var atAnchor = Resizer.ResizeBounds(Box, ResizeHandle.East, new Point(0, 0));
        var onTopOfAnchor = Resizer.ResizeBounds(Box, ResizeHandle.West, new Point(100, 0));

        Assert.Equal(Resizer.MinimumSize, atAnchor.Width);
        Assert.Equal(Resizer.MinimumSize, onTopOfAnchor.Width);
    }

    [Fact]
    public void RandomResizesKeepTheAnchorReachTheCursorAndStayAboveTheMinimum()
    {
        var random = new TestRandom(41);
        var handles = Enum.GetValues<ResizeHandle>();
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var start = new Bounds(Fixtures.Coordinate(random), Fixtures.Coordinate(random), Fixtures.Size(random), Fixtures.Size(random));
            var handle = handles[random.Next(handles.Length)];
            var cursor = Fixtures.RandomPoint(random);

            var result = Resizer.ResizeBounds(start, handle, cursor);

            Assert.True(result.Width >= Resizer.MinimumSize && result.Height >= Resizer.MinimumSize);
            AssertHorizontal(start, handle, cursor, result);
            AssertVertical(start, handle, cursor, result);
        }
    }

    private static void AssertHorizontal(Bounds start, ResizeHandle handle, Point cursor, Bounds result)
    {
        var anchor = handle is ResizeHandle.NorthWest or ResizeHandle.West or ResizeHandle.SouthWest ? start.Right
            : handle is ResizeHandle.NorthEast or ResizeHandle.East or ResizeHandle.SouthEast ? start.X
            : (double?)null;

        if (anchor is not { } fixedEdge)
        {
            Assert.Equal(start.X, result.X);
            Assert.Equal(start.Width, result.Width);
            return;
        }

        var movedEdge = cursor.X >= fixedEdge ? result.Right : result.X;
        var anchorEdge = cursor.X >= fixedEdge ? result.X : result.Right;
        Assert.Equal(fixedEdge, anchorEdge, 9);
        if (Math.Abs(cursor.X - fixedEdge) >= Resizer.MinimumSize)
        {
            Assert.Equal(cursor.X, movedEdge, 9);
        }
    }

    private static void AssertVertical(Bounds start, ResizeHandle handle, Point cursor, Bounds result)
    {
        var anchor = handle is ResizeHandle.NorthWest or ResizeHandle.North or ResizeHandle.NorthEast ? start.Bottom
            : handle is ResizeHandle.SouthWest or ResizeHandle.South or ResizeHandle.SouthEast ? start.Y
            : (double?)null;

        if (anchor is not { } fixedEdge)
        {
            Assert.Equal(start.Y, result.Y);
            Assert.Equal(start.Height, result.Height);
            return;
        }

        var movedEdge = cursor.Y >= fixedEdge ? result.Bottom : result.Y;
        var anchorEdge = cursor.Y >= fixedEdge ? result.Y : result.Bottom;
        Assert.Equal(fixedEdge, anchorEdge, 9);
        if (Math.Abs(cursor.Y - fixedEdge) >= Resizer.MinimumSize)
        {
            Assert.Equal(cursor.Y, movedEdge, 9);
        }
    }

    [Fact]
    public void ResizingARectangleSetsItsBoxOnly()
    {
        var rectangle = new Layer(Guid.NewGuid(), LayerType.Rectangle, 0, 0, 100, 50, Fixtures.Red);

        var resized = Resizer.Resize(rectangle, ResizeHandle.SouthEast, new Point(40, 30));

        Assert.Equal(new Bounds(0, 0, 40, 30), resized.Bounds);
        Assert.Null(resized.Points);
    }

    [Fact]
    public void ResizingAPathScalesItsPointsWithItsBox()
    {
        var path = LayerFactory.Path(Guid.NewGuid(), [new Point(0, 0), new Point(10, 0), new Point(10, 10)], Fixtures.Red);

        var resized = Resizer.Resize(path, ResizeHandle.SouthEast, new Point(20, 20));

        Assert.Equal(new Bounds(0, 0, 20, 20), resized.Bounds);
        Assert.Equal([new Point(0, 0), new Point(20, 0), new Point(20, 20)], resized.Points);
    }

    [Fact]
    public void AFlatPathDoesNotWidenFromZeroButStaysOnItsAxis()
    {
        var line = LayerFactory.Path(Guid.NewGuid(), [new Point(5, 0), new Point(5, 10)], Fixtures.Red);

        var resized = Resizer.Resize(line, ResizeHandle.East, new Point(20, 0));

        Assert.All(resized.Points!, p => Assert.Equal(5, p.X));
        Assert.Equal(new Bounds(5, 0, 15, 10), resized.Bounds);
    }

    [Fact]
    public void ResizedPathPointsStayInsideTheNewBox()
    {
        var random = new TestRandom(43);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var points = Enumerable.Range(0, random.Next(1, 6)).Select(_ => Fixtures.RandomPoint(random)).ToArray();
            var path = LayerFactory.Path(Guid.NewGuid(), points, Fixtures.Red);
            var handle = (ResizeHandle)random.Next(8);

            var resized = Resizer.Resize(path, handle, Fixtures.RandomPoint(random));

            Assert.All(resized.Points!, p => Assert.True(resized.Bounds.Inflate(1e-9).Contains(p)));
        }
    }

    [Fact]
    public void TranslationMovesTheBoxAndEveryPathPointAndLeavesTheOriginalAlone()
    {
        var path = LayerFactory.Path(Guid.NewGuid(), [new Point(0, 0), new Point(10, 10)], Fixtures.Red);

        var moved = Translator.Move(path, 5, -3);

        Assert.Equal(new Bounds(5, -3, 10, 10), moved.Bounds);
        Assert.Equal([new Point(5, -3), new Point(15, 7)], moved.Points);
        Assert.Equal(new Point(0, 0), path.Points![0]);
    }

    [Fact]
    public void TranslationOfARectangleKeepsItsSize()
    {
        var rectangle = new Layer(Guid.NewGuid(), LayerType.Rectangle, 0, 0, 10, 20, Fixtures.Red);

        var moved = Translator.Move(rectangle, 1, 2);

        Assert.Equal(new Bounds(1, 2, 10, 20), moved.Bounds);
        Assert.Null(moved.Points);
    }
}
