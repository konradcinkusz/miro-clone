using System.Globalization;
using MiroClone.Web.Api;
using MiroClone.Web.Canvas.Core;

namespace MiroClone.Web.Canvas.Editing;

/// <summary>
/// Turns the canvas service's wire state into the Core document. The wire types are copies, pinned by the round-trip tests.
/// A malformed state throws <see cref="FormatException"/>, which the page reports as a failed load.
/// </summary>
public static class CanvasStateMapper
{
    public static CanvasDocument ToDocument(CanvasStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // LayerIds runs bottom to top (ADR 0012), so the document order is the wire order.
        return CanvasDocument.Load(state.LayerIds.Select(id =>
            state.Layers.TryGetValue(id, out var layer)
                ? ToLayer(layer)
                : throw new FormatException($"Layer {id} is listed but not sent.")));
    }

    internal static Layer ToLayer(LayerDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Layer(
            dto.Id,
            ParseType(dto.Type),
            dto.X,
            dto.Y,
            dto.Width,
            dto.Height,
            new Rgb(Channel(dto.Fill.R), Channel(dto.Fill.G), Channel(dto.Fill.B)),
            dto.Value,
            dto.Points?.Select(ToPoint).ToArray());
    }

    private static LayerType ParseType(string type) =>
        Enum.TryParse<LayerType>(type, ignoreCase: false, out var parsed)
            ? parsed
            : throw new FormatException($"'{type}' is not a layer type.");

    private static Point ToPoint(double[] pair) =>
        pair is { Length: 2 } ? new Point(pair[0], pair[1]) : throw new FormatException("A point is an [x, y] pair.");

    private static byte Channel(int value) =>
        value is >= 0 and <= 255 ? (byte)value : throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Colour channel {value} is out of range."));
}
