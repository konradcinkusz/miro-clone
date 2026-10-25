using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class LayerFactoryTests
{
    private static readonly Rgb Fill = new(1, 2, 3);

    [Theory]
    [InlineData(LayerType.Rectangle, 120, 80)]
    [InlineData(LayerType.Ellipse, 120, 80)]
    [InlineData(LayerType.Text, 160, 40)]
    [InlineData(LayerType.Note, 200, 200)]
    public void AClickCreatesTheDefaultSizeAtThePoint(LayerType type, double width, double height)
    {
        var layer = LayerFactory.Create(Guid.NewGuid(), type, new Point(30, 40), new Point(30.2, 40.1), Fill);

        Assert.Equal(new Bounds(30, 40, width, height), layer.Bounds);
    }

    [Fact]
    public void ADragCreatesTheDrawnBoxWithDirectionIgnored()
    {
        var layer = LayerFactory.Create(Guid.NewGuid(), LayerType.Rectangle, new Point(50, 40), new Point(10, 20), Fill);

        Assert.Equal(new Bounds(10, 20, 40, 20), layer.Bounds);
        Assert.Equal(Fill, layer.Fill);
        Assert.Null(layer.Value);
        Assert.Null(layer.Points);
    }

    [Fact]
    public void ADragSmallerThanTheMinimumOnOneAxisIsClampedOnThatAxis()
    {
        var layer = LayerFactory.Create(Guid.NewGuid(), LayerType.Ellipse, new Point(0, 0), new Point(30, 0.2), Fill);

        Assert.Equal(new Bounds(0, 0, 30, Resizer.MinimumSize), layer.Bounds);
    }

    [Fact]
    public void TextAndNoteKeepTheirValueAndDefaultToEmpty()
    {
        var text = LayerFactory.Create(Guid.NewGuid(), LayerType.Text, new Point(0, 0), new Point(0, 0), Fill, "hello");
        var note = LayerFactory.Create(Guid.NewGuid(), LayerType.Note, new Point(0, 0), new Point(0, 0), Fill);

        Assert.Equal("hello", text.Value);
        Assert.Equal(string.Empty, note.Value);
    }

    [Fact]
    public void AShapeDropsAnyValueGivenToIt()
    {
        var layer = LayerFactory.Create(Guid.NewGuid(), LayerType.Rectangle, new Point(0, 0), new Point(10, 10), Fill, "ignored");

        Assert.Null(layer.Value);
    }

    [Fact]
    public void CreateRefusesPathsBecauseTheyNeedPoints()
    {
        Assert.Throws<ArgumentException>(() =>
            LayerFactory.Create(Guid.NewGuid(), LayerType.Path, new Point(0, 0), new Point(1, 1), Fill));
    }

    [Fact]
    public void APathIsBoundedByItsPointsAndCopiesThem()
    {
        var points = new List<Point> { new(0, 0), new(10, 4), new(6, 9) };

        var layer = LayerFactory.Path(Guid.NewGuid(), points, Fill);
        points.Add(new Point(500, 500));

        Assert.Equal(new Bounds(0, 0, 10, 9), layer.Bounds);
        Assert.Equal(3, layer.Points!.Count);
    }

    [Fact]
    public void APathNeedsAtLeastOnePoint()
    {
        Assert.Throws<ArgumentException>(() => LayerFactory.Path(Guid.NewGuid(), [], Fill));
    }

    [Fact]
    public void APathOfOnePointHasZeroSize()
    {
        var layer = LayerFactory.Path(Guid.NewGuid(), [new Point(4, 5)], Fill);

        Assert.Equal(new Bounds(4, 5, 0, 0), layer.Bounds);
    }
}
