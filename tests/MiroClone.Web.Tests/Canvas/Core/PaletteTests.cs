using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

/// <summary>The palette is the reference app's eight colours (ADR 0033). Its values are layer data and the text colour rule is Core's.</summary>
public sealed class PaletteTests
{
    [Fact]
    public void ThePaletteHasTheEightReferenceColours()
    {
        Assert.Equal(8, Palette.Colours.Count);
        Assert.Equal(8, Palette.Colours.Select(colour => colour.Name).Distinct().Count());
        Assert.Equal(new Rgb(243, 82, 35), Palette.Colours[0].Colour);
    }

    [Fact]
    public void TextOnADarkFillIsWhiteAndOnALightFillIsBlack()
    {
        Assert.Equal(new Rgb(255, 255, 255), Palette.ReadableOn(new Rgb(0, 0, 0)));
        Assert.Equal(Palette.Ink, Palette.ReadableOn(new Rgb(255, 255, 255)));
        Assert.Equal(Palette.Ink, Palette.ReadableOn(new Rgb(255, 249, 177)));
    }
}
