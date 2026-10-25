using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class CameraTests
{
    [Fact]
    public void TheDefaultCameraMapsWorldToScreenUnchanged()
    {
        var camera = Camera.Default;

        Assert.Equal(new Point(12, -7), camera.WorldToScreen(new Point(12, -7)));
        Assert.Equal(new Point(12, -7), camera.ScreenToWorld(new Point(12, -7)));
    }

    [Fact]
    public void PanningMovesEveryWorldPointByTheScreenDelta()
    {
        var camera = Camera.Default.Pan(30, -10);

        Assert.Equal(new Point(40, 0), camera.WorldToScreen(new Point(10, 10)));
    }

    [Fact]
    public void ScreenAndWorldAreInverses()
    {
        var random = new TestRandom(11);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var camera = new Camera(random.Between(-500, 500), random.Between(-500, 500), random.Between(Camera.MinimumZoom, Camera.MaximumZoom));
            var point = Fixtures.RandomPoint(random);

            var roundTrip = camera.ScreenToWorld(camera.WorldToScreen(point));

            Assert.Equal(point.X, roundTrip.X, 6);
            Assert.Equal(point.Y, roundTrip.Y, 6);
        }
    }

    [Fact]
    public void ZoomingKeepsThePointUnderTheCursorFixed()
    {
        var random = new TestRandom(23);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var camera = new Camera(random.Between(-300, 300), random.Between(-300, 300), random.Between(0.5, 3));
            var anchor = new Point(random.Between(0, 1200), random.Between(0, 800));
            var worldBefore = camera.ScreenToWorld(anchor);

            var zoomed = camera.ZoomAt(anchor, random.Between(0.5, 2));

            var worldAfter = zoomed.ScreenToWorld(anchor);
            Assert.Equal(worldBefore.X, worldAfter.X, 6);
            Assert.Equal(worldBefore.Y, worldAfter.Y, 6);
        }
    }

    [Fact]
    public void ZoomIsClampedToTheRange()
    {
        var camera = Camera.Default;

        Assert.Equal(Camera.MaximumZoom, camera.ZoomAt(new Point(0, 0), 1000).Zoom);
        Assert.Equal(Camera.MinimumZoom, camera.ZoomAt(new Point(0, 0), 0.000001).Zoom);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ZoomRejectsAFactorThatIsNotAPositiveFiniteNumber(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Camera.Default.ZoomAt(new Point(0, 0), factor));
    }
}
