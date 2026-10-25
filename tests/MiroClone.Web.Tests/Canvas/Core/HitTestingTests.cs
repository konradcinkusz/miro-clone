using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class HitTestingTests
{
    [Fact]
    public void ARectangleIsHitAnywhereInsideItsBox()
    {
        var rectangle = Fixtures.Rectangle(Guid.NewGuid(), 0, 0, 100, 50);

        Assert.True(HitTester.Hits(rectangle, new Point(50, 25), 0));
        Assert.True(HitTester.Hits(rectangle, new Point(0, 50), 0));
        Assert.False(HitTester.Hits(rectangle, new Point(100.5, 25), 0));
    }

    [Fact]
    public void ToleranceGrowsTheTarget()
    {
        var rectangle = Fixtures.Rectangle(Guid.NewGuid(), 0, 0, 100, 50);

        Assert.False(HitTester.Hits(rectangle, new Point(102, 25), 1));
        Assert.True(HitTester.Hits(rectangle, new Point(102, 25), 2));
    }

    [Fact]
    public void AnEllipseIsHitInsideItsOutlineNotInItsCorners()
    {
        var ellipse = new Layer(Guid.NewGuid(), LayerType.Ellipse, 0, 0, 100, 100, Fixtures.Red);

        Assert.True(HitTester.Hits(ellipse, new Point(50, 50), 0));
        Assert.True(HitTester.Hits(ellipse, new Point(100, 50), 0));
        Assert.False(HitTester.Hits(ellipse, new Point(5, 5), 0), "the corner of the box is outside the ellipse");
        Assert.True(HitTester.Hits(ellipse, new Point(5, 5), 10 * Math.Sqrt(2)), "tolerance reaches the outline");
    }

    [Fact]
    public void AZeroSizeEllipseWithNoToleranceIsNeverHit()
    {
        var ellipse = new Layer(Guid.NewGuid(), LayerType.Ellipse, 5, 5, 0, 0, Fixtures.Red);

        Assert.False(HitTester.Hits(ellipse, new Point(5, 5), 0));
        Assert.True(HitTester.Hits(ellipse, new Point(5, 5), 1));
    }

    [Fact]
    public void ATextOrNoteIsHitInsideItsBox()
    {
        var text = new Layer(Guid.NewGuid(), LayerType.Text, 0, 0, 160, 40, Fixtures.Red, "hi");
        var note = new Layer(Guid.NewGuid(), LayerType.Note, 0, 0, 200, 200, Fixtures.Red, "");

        Assert.True(HitTester.Hits(text, new Point(150, 35), 0));
        Assert.True(HitTester.Hits(note, new Point(199, 199), 0));
    }

    [Fact]
    public void APathIsHitNearItsStrokeOnly()
    {
        var path = LayerFactory.Path(Guid.NewGuid(), [new Point(0, 0), new Point(100, 0)], Fixtures.Red);

        Assert.True(HitTester.Hits(path, new Point(50, 0), 0));
        Assert.False(HitTester.Hits(path, new Point(50, 5), 1));
        Assert.True(HitTester.Hits(path, new Point(50, 5), 5));
        Assert.False(HitTester.Hits(path, new Point(105, 0), 4), "past the end of the stroke");
    }

    [Fact]
    public void ASinglePointPathIsHitAtThePoint()
    {
        var path = LayerFactory.Path(Guid.NewGuid(), [new Point(10, 10)], Fixtures.Red);

        Assert.True(HitTester.Hits(path, new Point(11, 10), 1));
        Assert.False(HitTester.Hits(path, new Point(12, 10), 1));
    }

    [Fact]
    public void ANegativeToleranceIsRejected()
    {
        var rectangle = Fixtures.Rectangle(Guid.NewGuid(), 0, 0, 10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => HitTester.Hits(rectangle, new Point(5, 5), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => HitTester.Hits(rectangle, new Point(5, 5), double.NaN));
    }

    [Fact]
    public void TheTopmostLayerUnderThePointWins()
    {
        var ids = Fixtures.Pool(3);
        var document = CanvasDocument.Load(
        [
            Fixtures.Rectangle(ids[0], 0, 0, 100, 100),
            Fixtures.Rectangle(ids[1], 50, 50, 100, 100),
            Fixtures.Rectangle(ids[2], 300, 300, 10, 10),
        ]);

        Assert.Equal(ids[1], HitTester.Topmost(document, new Point(60, 60), 0)!.Id);
        Assert.Equal(ids[0], HitTester.Topmost(document, new Point(10, 10), 0)!.Id);
        Assert.Null(HitTester.Topmost(document, new Point(200, 200), 0));
    }

    [Fact]
    public void RandomRectanglesAreHitAtTheirCentreAndMissedBeyondTheTolerance()
    {
        var random = new TestRandom(31);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var box = new Bounds(Fixtures.Coordinate(random), Fixtures.Coordinate(random), Fixtures.Size(random), Fixtures.Size(random));
            var rectangle = new Layer(Guid.NewGuid(), LayerType.Rectangle, box.X, box.Y, box.Width, box.Height, Fixtures.Red);
            var tolerance = random.Between(0, 5);
            var centre = new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));

            Assert.True(HitTester.Hits(rectangle, centre, tolerance));
            Assert.False(HitTester.Hits(rectangle, new Point(box.Right + tolerance + 1, centre.Y), tolerance));
        }
    }

    [Fact]
    public void RandomEllipsesAreHitAtTheirCentreAndMissedFarOutside()
    {
        var random = new TestRandom(37);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var box = new Bounds(Fixtures.Coordinate(random), Fixtures.Coordinate(random), Fixtures.Size(random), Fixtures.Size(random));
            var ellipse = new Layer(Guid.NewGuid(), LayerType.Ellipse, box.X, box.Y, box.Width, box.Height, Fixtures.Red);
            var centre = new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));

            Assert.True(HitTester.Hits(ellipse, centre, 0));
            Assert.False(HitTester.Hits(ellipse, new Point(box.Right + 1, box.Bottom + 1), 0));
        }
    }

    [Fact]
    public void TheNetSelectsEveryLayerItTouchesBottomToTop()
    {
        var ids = Fixtures.Pool(4);
        var document = CanvasDocument.Load(
        [
            Fixtures.Rectangle(ids[0], 0, 0, 10, 10),
            Fixtures.Rectangle(ids[1], 100, 100, 10, 10),
            Fixtures.Rectangle(ids[2], 5, 5, 10, 10),
            Fixtures.Rectangle(ids[3], 500, 500, 10, 10),
        ]);

        var selected = Marquee.Select(document, Marquee.Net(new Point(-5, -5), new Point(10, 15)));

        Assert.Equal([ids[0], ids[2]], selected);
    }

    [Fact]
    public void TheNetIncludesALayerItOnlyGrazes()
    {
        var id = Guid.NewGuid();
        var document = CanvasDocument.Load([Fixtures.Rectangle(id, 10, 10, 10, 10)]);

        Assert.Equal([id], Marquee.Select(document, Marquee.Net(new Point(20, 20), new Point(30, 30))));
        Assert.Empty(Marquee.Select(document, Marquee.Net(new Point(21, 21), new Point(30, 30))));
    }

    [Fact]
    public void TheNetIsNormalisedWhateverTheDragDirection()
    {
        Assert.Equal(new Bounds(10, 20, 30, 40), Marquee.Net(new Point(40, 60), new Point(10, 20)));
    }
}
