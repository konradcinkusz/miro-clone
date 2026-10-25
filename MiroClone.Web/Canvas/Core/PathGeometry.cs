namespace MiroClone.Web.Canvas.Core;

/// <summary>Distances to points, segments and polylines, in world units.</summary>
public static class PathGeometry
{
    public static double Distance(Point a, Point b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    public static double DistanceToSegment(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared == 0)
        {
            return Distance(point, start);
        }

        var t = Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared, 0, 1);
        return Distance(point, new Point(start.X + (t * dx), start.Y + (t * dy)));
    }

    /// <summary>The distance to the nearest segment. A single point counts as a one-point polyline. Empty gives infinity.</summary>
    public static double DistanceToPolyline(Point point, IReadOnlyList<Point> polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);

        if (polyline.Count == 1)
        {
            return Distance(point, polyline[0]);
        }

        var nearest = double.PositiveInfinity;
        for (var i = 0; i + 1 < polyline.Count; i++)
        {
            nearest = Math.Min(nearest, DistanceToSegment(point, polyline[i], polyline[i + 1]));
        }

        return nearest;
    }
}
