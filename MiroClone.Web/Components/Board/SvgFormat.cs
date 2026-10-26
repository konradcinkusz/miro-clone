using System.Globalization;
using MiroClone.Web.Canvas.Core;

namespace MiroClone.Web.Components.Board;

/// <summary>
/// Number and point text for SVG attributes. Always invariant culture, so a decimal comma in the browser's locale can never
/// reach an attribute and break the drawing.
/// </summary>
public static class SvgFormat
{
    public static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Points(IEnumerable<Point> points) =>
        string.Join(" ", points.Select(point => $"{Num(point.X)},{Num(point.Y)}"));
}
