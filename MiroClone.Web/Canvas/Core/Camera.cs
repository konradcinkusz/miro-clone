namespace MiroClone.Web.Canvas.Core;

/// <summary>
/// The view onto the canvas. A world point maps to the screen as <c>world * Zoom + Offset</c>.
/// Use <see cref="Default"/> for the start state; the zero value has a zero zoom and is not a valid camera.
/// </summary>
public readonly record struct Camera(double OffsetX, double OffsetY, double Zoom)
{
    public const double MinimumZoom = 0.1;
    public const double MaximumZoom = 8;

    public static Camera Default { get; } = new(0, 0, 1);

    public Point ScreenToWorld(Point screen) =>
        new((screen.X - OffsetX) / Zoom, (screen.Y - OffsetY) / Zoom);

    public Point WorldToScreen(Point world) =>
        new(world.X * Zoom + OffsetX, world.Y * Zoom + OffsetY);

    /// <summary>Moves the view by a screen-pixel delta. The world point under the pointer moves with the view.</summary>
    public Camera Pan(double deltaX, double deltaY) =>
        this with { OffsetX = OffsetX + deltaX, OffsetY = OffsetY + deltaY };

    /// <summary>
    /// Multiplies the zoom by <paramref name="factor"/>, clamped to the zoom range. The world point under
    /// <paramref name="anchorScreen"/> stays under it.
    /// </summary>
    public Camera ZoomAt(Point anchorScreen, double factor)
    {
        if (!(factor > 0) || !double.IsFinite(factor))
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The zoom factor must be a positive finite number.");
        }

        var zoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        var world = ScreenToWorld(anchorScreen);
        return new Camera(anchorScreen.X - world.X * zoom, anchorScreen.Y - world.Y * zoom, zoom);
    }
}
