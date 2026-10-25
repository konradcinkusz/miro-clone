namespace MiroClone.Web.Canvas.Core;

/// <summary>Builds new layers from a pointer gesture. A click, with no drag, creates the type's default size.</summary>
public static class LayerFactory
{
    /// <summary>Creates a rectangle, ellipse, text or note from a drag. Paths come from <see cref="Path"/>.</summary>
    public static Layer Create(Guid id, LayerType type, Point start, Point end, Rgb fill, string? value = null)
    {
        if (type == LayerType.Path)
        {
            throw new ArgumentException("Paths are created from their points with Path().", nameof(type));
        }

        var drawn = Bounds.FromCorners(start, end);
        var isClick = drawn.Width < Resizer.MinimumSize && drawn.Height < Resizer.MinimumSize;
        var box = isClick
            ? new Bounds(start.X, start.Y, DefaultWidth(type), DefaultHeight(type))
            : new Bounds(
                drawn.X,
                drawn.Y,
                Math.Max(drawn.Width, Resizer.MinimumSize),
                Math.Max(drawn.Height, Resizer.MinimumSize));

        var text = type is LayerType.Text or LayerType.Note ? value ?? string.Empty : null;
        return new Layer(id, type, box.X, box.Y, box.Width, box.Height, fill, text);
    }

    /// <summary>Creates a pencil path. The points are stored as given; simplify them first with <see cref="PathSimplifier"/>.</summary>
    public static Layer Path(Guid id, IReadOnlyList<Point> points, Rgb fill)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("A path needs at least one point.", nameof(points));
        }

        var box = Bounds.FromPoints(points);
        return new Layer(id, LayerType.Path, box.X, box.Y, box.Width, box.Height, fill, null, points.ToArray());
    }

    private static double DefaultWidth(LayerType type) => type switch
    {
        LayerType.Text => 160,
        LayerType.Note => 200,
        _ => 120,
    };

    private static double DefaultHeight(LayerType type) => type switch
    {
        LayerType.Text => 40,
        LayerType.Note => 200,
        _ => 80,
    };
}
