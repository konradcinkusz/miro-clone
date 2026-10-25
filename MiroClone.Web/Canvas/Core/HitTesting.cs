namespace MiroClone.Web.Canvas.Core;

/// <summary>Which layers a pointer hits. Hit-testing uses the shape itself. Paths are tested against their stroke.</summary>
public static class HitTester
{
    /// <summary>
    /// True when the world point lies on the layer. Filled shapes and text count anywhere inside their box.
    /// Ellipses use their outline and interior. Paths use distance to the stroke. Tolerance is in world units and grows the target.
    /// </summary>
    public static bool Hits(Layer layer, Point world, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (!(tolerance >= 0) || !double.IsFinite(tolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be zero or a positive finite number.");
        }

        return layer.Type switch
        {
            LayerType.Ellipse => HitsEllipse(layer.Bounds, world, tolerance),
            LayerType.Path => PathGeometry.DistanceToPolyline(world, layer.Points ?? []) <= tolerance,
            _ => layer.Bounds.Inflate(tolerance).Contains(world),
        };
    }

    /// <summary>The topmost layer under the point, or null.</summary>
    public static Layer? Topmost(CanvasDocument document, Point world, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(document);

        Layer? hit = null;
        foreach (var layer in document.Ordered())
        {
            if (Hits(layer, world, tolerance))
            {
                hit = layer;
            }
        }

        return hit;
    }

    private static bool HitsEllipse(Bounds box, Point world, double tolerance)
    {
        var radiusX = (box.Width / 2) + tolerance;
        var radiusY = (box.Height / 2) + tolerance;
        if (radiusX <= 0 || radiusY <= 0)
        {
            return false;
        }

        var dx = (world.X - (box.X + (box.Width / 2))) / radiusX;
        var dy = (world.Y - (box.Y + (box.Height / 2))) / radiusY;
        return (dx * dx) + (dy * dy) <= 1;
    }
}

/// <summary>The marquee (net) selection: the layers whose boxes meet a drag rectangle.</summary>
public static class Marquee
{
    public static Bounds Net(Point start, Point current) => Bounds.FromCorners(start, current);

    /// <summary>Layers whose bounding box intersects the net. Touching edges count, so a sliver still selects. Returns ids bottom to top.</summary>
    public static IReadOnlyList<Guid> Select(CanvasDocument document, Bounds net)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Ordered()
            .Where(layer => layer.Bounds.Intersects(net))
            .Select(layer => layer.Id)
            .ToArray();
    }
}
