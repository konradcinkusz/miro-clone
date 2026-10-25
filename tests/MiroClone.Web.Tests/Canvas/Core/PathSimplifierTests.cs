using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class PathSimplifierTests
{
    [Fact]
    public void ShortPathsAreReturnedUnchanged()
    {
        var points = new[] { new Point(0, 0), new Point(9, 9) };

        Assert.Equal(points, PathSimplifier.Simplify(points, 100));
    }

    [Fact]
    public void CollinearPointsCollapseToTheirEnds()
    {
        var points = Enumerable.Range(0, 20).Select(i => new Point(i, 2 * i)).ToArray();

        var simplified = PathSimplifier.Simplify(points, 0.001);

        Assert.Equal([points[0], points[^1]], simplified);
    }

    [Fact]
    public void ACornerLargerThanTheToleranceIsKept()
    {
        var points = new[] { new Point(0, 0), new Point(5, 0.1), new Point(10, 0) };

        Assert.Equal([points[0], points[2]], PathSimplifier.Simplify(points, 1));
        Assert.Equal(points, PathSimplifier.Simplify(points, 0.01));
    }

    [Fact]
    public void EveryDroppedPointLiesWithinTheToleranceOfTheResult()
    {
        var random = new TestRandom(53);
        for (var i = 0; i < Fixtures.Iterations; i++)
        {
            var points = Enumerable.Range(0, random.Next(3, 60)).Select(_ => Fixtures.RandomPoint(random)).ToArray();
            var tolerance = random.Between(0.1, 20);

            var simplified = PathSimplifier.Simplify(points, tolerance);

            Assert.Equal(points[0], simplified[0]);
            Assert.Equal(points[^1], simplified[^1]);
            Assert.Subset(new HashSet<Point>(points), new HashSet<Point>(simplified));
            foreach (var point in points)
            {
                Assert.True(PathGeometry.DistanceToPolyline(point, simplified) <= tolerance + 1e-9);
            }
        }
    }

    [Fact]
    public void ANegativeOrNonFiniteToleranceIsRejected()
    {
        var points = new[] { new Point(0, 0), new Point(1, 1), new Point(2, 0) };

        Assert.Throws<ArgumentOutOfRangeException>(() => PathSimplifier.Simplify(points, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathSimplifier.Simplify(points, double.NaN));
        Assert.Throws<ArgumentNullException>(() => PathSimplifier.Simplify(null!, 1));
    }

    [Fact]
    public void ALongStrokeIsSimplifiedWithoutRecursing()
    {
        var points = Enumerable.Range(0, 200_000).Select(i => new Point(i, i % 2 == 0 ? 0 : 1)).ToArray();

        var simplified = PathSimplifier.Simplify(points, 2);

        Assert.Equal([points[0], points[^1]], simplified);
    }

    [Fact]
    public void DistanceToASegmentIsMeasuredToTheNearestPointOnIt()
    {
        Assert.Equal(3, PathGeometry.DistanceToSegment(new Point(5, 3), new Point(0, 0), new Point(10, 0)), 9);
        Assert.Equal(5, PathGeometry.DistanceToSegment(new Point(13, 4), new Point(10, 0), new Point(10, 0)), 9);
        Assert.Equal(5, PathGeometry.DistanceToSegment(new Point(-3, 4), new Point(0, 0), new Point(0, 0)), 9);
    }

    [Fact]
    public void AnEmptyPolylineIsInfinitelyFar()
    {
        Assert.Equal(double.PositiveInfinity, PathGeometry.DistanceToPolyline(new Point(0, 0), []));
    }
}
