using Bunit;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Components.Board;
using Xunit;

namespace MiroClone.Web.Tests.Components.Board;

/// <summary>Each layer type draws its own SVG element, with the box, colour and text the Core layer holds.</summary>
public sealed class LayerRenderingTests : BunitContext
{
    private static readonly Guid Id = Guid.Parse("00000000-0000-4000-8000-0000000000c1");
    private static readonly Rgb Fill = new(1, 2, 3);

    [Fact]
    public void ARectangleDrawsARectWithTheLayerBoxAndFill()
    {
        var page = Render(new Layer(Id, LayerType.Rectangle, 10, 20, 100, 50, Fill));

        var rect = page.Find("[data-testid='layer-rectangle']");
        Assert.Equal("rect", rect.TagName.ToLowerInvariant());
        Assert.Equal("10", rect.GetAttribute("x"));
        Assert.Equal("50", rect.GetAttribute("height"));
        Assert.Equal("#010203", rect.GetAttribute("fill"));
        Assert.Equal(Id.ToString(), rect.GetAttribute("data-layer-id"));
    }

    [Fact]
    public void AnEllipseIsDrawnFromItsCentreAndRadii()
    {
        var page = Render(new Layer(Id, LayerType.Ellipse, 10, 20, 100, 50, Fill));

        var ellipse = page.Find("[data-testid='layer-ellipse']");
        Assert.Equal("60", ellipse.GetAttribute("cx"));
        Assert.Equal("45", ellipse.GetAttribute("cy"));
        Assert.Equal("50", ellipse.GetAttribute("rx"));
        Assert.Equal("25", ellipse.GetAttribute("ry"));
    }

    [Fact]
    public void ATextLayerShowsItsValueInsideItsBox()
    {
        var page = Render(new Layer(Id, LayerType.Text, 10, 20, 160, 40, Fill, "Hello & welcome"));

        var text = page.Find("[data-testid='layer-text']");
        Assert.Equal("Hello & welcome", text.TextContent);
        Assert.Equal("18", text.GetAttribute("x"));
        Assert.Equal("40", text.GetAttribute("y"));
    }

    [Fact]
    public void ANoteDrawsItsBoxAndItsTextInTheCorner()
    {
        var page = Render(new Layer(Id, LayerType.Note, 0, 0, 200, 200, Fill, "Remember"));

        Assert.Equal("#010203", page.Find("[data-testid='layer-note']").GetAttribute("fill"));
        var text = page.Find("[data-testid='note-text']");
        Assert.Equal("Remember", text.TextContent);
        Assert.Equal("hanging", text.GetAttribute("dominant-baseline"));
    }

    [Fact]
    public void APathDrawsAPolylineWithItsPointsAndColourAsStroke()
    {
        var path = LayerFactory.Path(Id, [new Point(0, 0), new Point(1.5, 2)], Fill);

        var page = Render(path);

        var polyline = page.Find("[data-testid='layer-path']");
        Assert.Equal("0,0 1.5,2", polyline.GetAttribute("points"));
        Assert.Equal("#010203", polyline.GetAttribute("stroke"));
    }

    [Fact]
    public void NumbersAreWrittenInvariantlyWhateverTheCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new("pl-PL");
        try
        {
            var page = Render(new Layer(Id, LayerType.Rectangle, 1.5, 2.25, 10, 10, Fill));

            Assert.Equal("1.5", page.Find("[data-testid='layer-rectangle']").GetAttribute("x"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    private IRenderedComponent<LayerPreview> Render(Layer layer) =>
        Render<LayerPreview>(parameters => parameters.Add(component => component.Layer, layer));
}
