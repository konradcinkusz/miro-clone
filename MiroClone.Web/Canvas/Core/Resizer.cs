namespace MiroClone.Web.Canvas.Core;

public enum ResizeHandle
{
    NorthWest,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
}

/// <summary>
/// Eight handles around a selection box, and resizing from them. A handle keeps its anchor edge fixed and moves
/// the opposite edge to the cursor. Dragging past the anchor flips the box rather than inverting it. Each call
/// reads only the start box and the cursor, so the result depends on where the drag began and where it is now.
/// </summary>
public static class Resizer
{
    /// <summary>Smallest width or height a resize or a drawn layer can have, in world units.</summary>
    public const double MinimumSize = 1;

    private static readonly ResizeHandle[] HandleOrder =
    [
        ResizeHandle.NorthWest,
        ResizeHandle.NorthEast,
        ResizeHandle.SouthEast,
        ResizeHandle.SouthWest,
        ResizeHandle.North,
        ResizeHandle.East,
        ResizeHandle.South,
        ResizeHandle.West,
    ];

    public static Point HandlePosition(Bounds box, ResizeHandle handle) => handle switch
    {
        ResizeHandle.NorthWest => new Point(box.X, box.Y),
        ResizeHandle.North => new Point(box.X + (box.Width / 2), box.Y),
        ResizeHandle.NorthEast => new Point(box.Right, box.Y),
        ResizeHandle.East => new Point(box.Right, box.Y + (box.Height / 2)),
        ResizeHandle.SouthEast => new Point(box.Right, box.Bottom),
        ResizeHandle.South => new Point(box.X + (box.Width / 2), box.Bottom),
        ResizeHandle.SouthWest => new Point(box.X, box.Bottom),
        ResizeHandle.West => new Point(box.X, box.Y + (box.Height / 2)),
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, null),
    };

    /// <summary>The handle under the point, corners first, within a square of <paramref name="tolerance"/> around each one.</summary>
    public static ResizeHandle? HandleAt(Bounds box, Point point, double tolerance)
    {
        foreach (var handle in HandleOrder)
        {
            var position = HandlePosition(box, handle);
            if (Math.Abs(point.X - position.X) <= tolerance && Math.Abs(point.Y - position.Y) <= tolerance)
            {
                return handle;
            }
        }

        return null;
    }

    /// <summary>The box after dragging <paramref name="handle"/> of <paramref name="start"/> to <paramref name="cursor"/>.</summary>
    public static Bounds ResizeBounds(Bounds start, ResizeHandle handle, Point cursor)
    {
        var (x, width) = handle switch
        {
            ResizeHandle.NorthWest or ResizeHandle.West or ResizeHandle.SouthWest => Span(start.Right, cursor.X),
            ResizeHandle.NorthEast or ResizeHandle.East or ResizeHandle.SouthEast => Span(start.X, cursor.X),
            _ => (start.X, start.Width),
        };

        var (y, height) = handle switch
        {
            ResizeHandle.NorthWest or ResizeHandle.North or ResizeHandle.NorthEast => Span(start.Bottom, cursor.Y),
            ResizeHandle.SouthWest or ResizeHandle.South or ResizeHandle.SouthEast => Span(start.Y, cursor.Y),
            _ => (start.Y, start.Height),
        };

        return new Bounds(x, y, width, height);
    }

    /// <summary>
    /// Resizes a layer. A path's points scale with its box. A path that is flat on an axis keeps its points on
    /// that axis, so it does not widen from zero there.
    /// </summary>
    public static Layer Resize(Layer layer, ResizeHandle handle, Point cursor)
    {
        ArgumentNullException.ThrowIfNull(layer);

        var start = layer.Bounds;
        var target = ResizeBounds(start, handle, cursor);
        if (layer.Type != LayerType.Path)
        {
            return layer.WithBounds(target);
        }

        var scaleX = start.Width > 0 ? target.Width / start.Width : 1;
        var scaleY = start.Height > 0 ? target.Height / start.Height : 1;
        var points = (layer.Points ?? [])
            .Select(p => new Point(target.X + ((p.X - start.X) * scaleX), target.Y + ((p.Y - start.Y) * scaleY)))
            .ToArray();

        return layer.WithBounds(target) with { Points = points };
    }

    /// <summary>
    /// Maps the cursor to one axis. Before the anchor the box spans from the cursor to the anchor. Past it the box
    /// flips to the other side. Either way it is at least <see cref="MinimumSize"/> long.
    /// </summary>
    private static (double Start, double Length) Span(double anchor, double cursor)
    {
        if (cursor >= anchor)
        {
            return (anchor, Math.Max(cursor - anchor, MinimumSize));
        }

        var length = Math.Max(anchor - cursor, MinimumSize);
        return (anchor - length, length);
    }
}

/// <summary>Moves layers. Translation keeps size and shape, and moves path points with the box.</summary>
public static class Translator
{
    public static Layer Move(Layer layer, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(layer);

        var points = layer.Points?.Select(p => p.Offset(deltaX, deltaY)).ToArray();
        return layer with { X = layer.X + deltaX, Y = layer.Y + deltaY, Points = points };
    }
}
