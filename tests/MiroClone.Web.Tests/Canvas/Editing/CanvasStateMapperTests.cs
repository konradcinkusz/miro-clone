using MiroClone.Web.Api;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Editing;

/// <summary>The wire state becomes a Core document in layerIds order, and a malformed state is refused rather than drawn.</summary>
public sealed class CanvasStateMapperTests
{
    private static readonly Guid Bottom = Guid.Parse("00000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Top = Guid.Parse("00000000-0000-4000-8000-0000000000a2");

    [Fact]
    public void LayersKeepTheWireOrderBottomToTopAndTheirFieldsAndColours()
    {
        var state = State(
            [Bottom, Top],
            Rectangle(Bottom, fill: new FillDto(255, 0, 16)),
            Path(Top));

        var document = CanvasStateMapper.ToDocument(state);

        Assert.Equal([Bottom, Top], document.Order);
        Assert.True(document.TryGet(Bottom, out var rectangle));
        Assert.Equal(new Rgb(255, 0, 16), rectangle.Fill);
        Assert.Equal(new Bounds(10, 20, 100, 50), rectangle.Bounds);
        Assert.True(document.TryGet(Top, out var path));
        Assert.Equal(LayerType.Path, path.Type);
        Assert.Equal([new Point(0, 0), new Point(5, 5)], path.Points);
    }

    [Fact]
    public void TextKeepsItsValue()
    {
        var text = new LayerDto(Bottom, "Text", 0, 0, 160, 40, new FillDto(0, 0, 0), "Hello", null);

        var document = CanvasStateMapper.ToDocument(State([Bottom], text));

        Assert.True(document.TryGet(Bottom, out var layer));
        Assert.Equal("Hello", layer.Value);
    }

    [Fact]
    public void AnUnknownLayerTypeIsRefused()
    {
        var state = State([Bottom], Rectangle(Bottom) with { Type = "Triangle" });

        Assert.Throws<FormatException>(() => CanvasStateMapper.ToDocument(state));
    }

    [Theory]
    [InlineData(256, 0, 0)]
    [InlineData(0, -1, 0)]
    public void AColourChannelOutsideAByteIsRefused(int r, int g, int b)
    {
        var state = State([Bottom], Rectangle(Bottom, fill: new FillDto(r, g, b)));

        Assert.Throws<FormatException>(() => CanvasStateMapper.ToDocument(state));
    }

    [Fact]
    public void ALayerListedButNotSentIsRefused()
    {
        var state = new CanvasStateDto(1, [Bottom], new Dictionary<Guid, LayerDto>());

        Assert.Throws<FormatException>(() => CanvasStateMapper.ToDocument(state));
    }

    [Fact]
    public void APointThatIsNotAPairIsRefused()
    {
        var state = State([Top], Path(Top) with { Points = [new double[] { 1, 2, 3 }] });

        Assert.Throws<FormatException>(() => CanvasStateMapper.ToDocument(state));
    }

    private static CanvasStateDto State(Guid[] order, params LayerDto[] layers) =>
        new(1, order, layers.ToDictionary(layer => layer.Id));

    private static LayerDto Rectangle(Guid id, FillDto? fill = null) =>
        new(id, "Rectangle", 10, 20, 100, 50, fill ?? new FillDto(1, 2, 3), null, null);

    private static LayerDto Path(Guid id) =>
        new(id, "Path", 0, 0, 5, 5, new FillDto(9, 9, 9), null, [new double[] { 0, 0 }, new double[] { 5, 5 }]);
}
