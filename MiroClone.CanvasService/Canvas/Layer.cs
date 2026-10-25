using System.Text.Json.Serialization;

namespace MiroClone.CanvasService.Canvas;

/// <summary>The shapes a board holds. Serialised by name, as the frozen contract spells them.</summary>
public enum LayerType
{
    Rectangle,
    Ellipse,
    Text,
    Note,
    Path,
}

/// <summary>A colour component triple. Each channel is a byte, so the JSON range is enforced by the type.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>A point in the object form the contract uses for presence cursors: <c>{ "x": 1, "y": 2 }</c>.</summary>
public readonly record struct Point(double X, double Y);

/// <summary>A path vertex in the pair form the contract uses for pencil strokes: <c>[x, y]</c>.</summary>
[JsonConverter(typeof(PathPointJsonConverter))]
public readonly record struct PathPoint(double X, double Y);

/// <summary>
/// One layer on a board. A record, so the value is compared and copied, never shared by reference: the aggregate
/// stores its own copy and every snapshot is a copy too.
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
    IReadOnlyList<PathPoint>? Points = null);
