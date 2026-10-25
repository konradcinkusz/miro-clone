using System.Globalization;

namespace MiroClone.Web.Canvas.Core;

/// <summary>A position. World units on the canvas unless a member says it takes screen pixels.</summary>
public readonly record struct Point(double X, double Y)
{
    public Point Offset(double dx, double dy) => new(X + dx, Y + dy);
}

/// <summary>An axis-aligned box. Width and Height are never negative.</summary>
public readonly record struct Bounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public static Bounds FromCorners(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static Bounds FromPoints(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var box = (Bounds?)null;
        foreach (var point in points)
        {
            var single = new Bounds(point.X, point.Y, 0, 0);
            box = box is { } current ? current.Join(single) : single;
        }

        return box ?? throw new ArgumentException("At least one point is required.", nameof(points));
    }

    /// <summary>The smallest box holding every input, or null when there are none.</summary>
    public static Bounds? Union(IEnumerable<Bounds> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        Bounds? result = null;
        foreach (var box in boxes)
        {
            result = result is { } current ? current.Join(box) : box;
        }

        return result;
    }

    public Bounds Join(Bounds other)
    {
        var x = Math.Min(X, other.X);
        var y = Math.Min(Y, other.Y);
        return new Bounds(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }

    /// <summary>Edges count as inside, so a point on the border is contained.</summary>
    public bool Contains(Point point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>Touching edges count as intersecting.</summary>
    public bool Intersects(Bounds other) =>
        X <= other.Right && other.X <= Right && Y <= other.Bottom && other.Y <= Bottom;

    public Bounds Inflate(double amount) =>
        new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);
}

/// <summary>An opaque colour. Serialised as {r,g,b} with 0..255 integers.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string hex) =>
        TryParse(hex, out var rgb) ? rgb : throw new FormatException($"'{hex}' is not a #rrggbb colour.");

    /// <summary>Accepts six hex digits with or without a leading '#'. Case-insensitive.</summary>
    public static bool TryParse(string? hex, out Rgb rgb)
    {
        rgb = default;
        if (hex is null)
        {
            return false;
        }

        var digits = hex.StartsWith('#') ? hex.AsSpan(1) : hex.AsSpan();
        if (digits.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(digits[..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(digits.Slice(2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(digits[4..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = new Rgb(r, g, b);
        return true;
    }

    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";
}
