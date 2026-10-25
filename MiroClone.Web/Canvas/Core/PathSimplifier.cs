namespace MiroClone.Web.Canvas.Core;

/// <summary>
/// Ramer–Douglas–Peucker simplification for pencil strokes. Keeps the first and last point. Every dropped point
/// lies within <c>tolerance</c> of the simplified path. Iterative, so a long stroke cannot overflow the stack.
/// </summary>
public static class PathSimplifier
{
    public static IReadOnlyList<Point> Simplify(IReadOnlyList<Point> points, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!(tolerance >= 0) || !double.IsFinite(tolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be zero or a positive finite number.");
        }

        if (points.Count <= 2)
        {
            return points.ToArray();
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        var ranges = new Stack<(int First, int Last)>();
        ranges.Push((0, points.Count - 1));

        while (ranges.Count > 0)
        {
            var (first, last) = ranges.Pop();
            var furthest = -1;
            var furthestDistance = 0.0;

            for (var i = first + 1; i < last; i++)
            {
                var distance = PathGeometry.DistanceToSegment(points[i], points[first], points[last]);
                if (distance > furthestDistance)
                {
                    furthest = i;
                    furthestDistance = distance;
                }
            }

            if (furthest >= 0 && furthestDistance > tolerance)
            {
                keep[furthest] = true;
                ranges.Push((first, furthest));
                ranges.Push((furthest, last));
            }
        }

        return points.Where((_, index) => keep[index]).ToArray();
    }
}
