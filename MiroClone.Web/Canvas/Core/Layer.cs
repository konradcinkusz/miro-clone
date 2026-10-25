namespace MiroClone.Web.Canvas.Core;

public enum LayerType
{
    Rectangle,
    Ellipse,
    Text,
    Note,
    Path,
}

/// <summary>
/// One object on a board. Immutable: change a layer by building a copy with <c>with</c>.
/// Paths carry their points in world coordinates; X, Y, Width and Height are the box around them.
/// </summary>
public sealed record Layer(
    Guid Id,
    LayerType Type,
    double X,
    double Y,
    double Width,
    double Height,
    Rgb Fill,
    string? Value = null,
    IReadOnlyList<Point>? Points = null)
{
    public Bounds Bounds => new(X, Y, Width, Height);

    public Layer WithBounds(Bounds bounds) =>
        this with { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };

    /// <summary>Compares every field, including path points by value (record equality compares the list by reference).</summary>
    public bool HasSameContent(Layer other) =>
        Id == other.Id &&
        Type == other.Type &&
        X == other.X &&
        Y == other.Y &&
        Width == other.Width &&
        Height == other.Height &&
        Fill == other.Fill &&
        Value == other.Value &&
        SamePoints(Points, other.Points);

    private static bool SamePoints(IReadOnlyList<Point>? left, IReadOnlyList<Point>? right) =>
        left is null || right is null ? left is null && right is null : left.SequenceEqual(right);
}
