namespace MiroClone.Web.Canvas.Core;

/// <param name="Name">The colour's name, shown as the swatch label.</param>
/// <param name="Colour">The fill it gives a layer.</param>
public sealed record PaletteColour(string Name, Rgb Colour);

/// <summary>
/// The colours a layer can take, taken from the reference app's colour picker (<c>miro-clone-nextjs</c>, the board's
/// <c>color-picker.tsx</c>). They are layer data: the markup gives them to SVG attributes and to one custom property on a
/// swatch, and no stylesheet names them. Chrome colours stay on the tokens (ADR 0033).
/// </summary>
public static class Palette
{
    public static readonly IReadOnlyList<PaletteColour> Colours =
    [
        new("Red", new Rgb(243, 82, 35)),
        new("Yellow", new Rgb(255, 249, 177)),
        new("Green", new Rgb(68, 202, 99)),
        new("Blue", new Rgb(39, 142, 237)),
        new("Purple", new Rgb(155, 105, 245)),
        new("Orange", new Rgb(252, 142, 42)),
        new("Black", new Rgb(0, 0, 0)),
        new("White", new Rgb(255, 255, 255)),
    ];

    /// <summary>The fill of a new text layer and a new pencil stroke: dark enough to read on the canvas surface.</summary>
    public static readonly Rgb Ink = new(0, 0, 0);

    /// <summary>
    /// Black or white, whichever reads better on <paramref name="background"/>. Uses the Rec. 709 luma of the sRGB values,
    /// which is close enough for choosing a label colour.
    /// </summary>
    public static Rgb ReadableOn(Rgb background)
    {
        var luma = 0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B;
        return luma > 140 ? Ink : new Rgb(255, 255, 255);
    }
}
