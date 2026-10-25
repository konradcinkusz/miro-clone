using MiroClone.Web.Canvas.Core;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

public sealed class GeometryTests
{
    [Fact]
    public void FromCornersNormalisesReversedCorners()
    {
        var box = Bounds.FromCorners(new Point(50, 40), new Point(10, 20));

        Assert.Equal(new Bounds(10, 20, 40, 20), box);
    }

    [Fact]
    public void FromPointsCoversEveryPoint()
    {
        var box = Bounds.FromPoints([new Point(3, 9), new Point(-2, 4), new Point(7, 1)]);

        Assert.Equal(new Bounds(-2, 1, 9, 8), box);
    }

    [Fact]
    public void FromPointsOfOnePointHasZeroSize()
    {
        Assert.Equal(new Bounds(4, 5, 0, 0), Bounds.FromPoints([new Point(4, 5)]));
    }

    [Fact]
    public void FromPointsRejectsAnEmptySequence()
    {
        Assert.Throws<ArgumentException>(() => Bounds.FromPoints([]));
    }

    [Fact]
    public void UnionOfNothingIsNull()
    {
        Assert.Null(Bounds.Union([]));
    }

    [Fact]
    public void UnionCoversEveryBox()
    {
        var union = Bounds.Union([new Bounds(0, 0, 10, 10), new Bounds(20, -5, 5, 5)]);

        Assert.Equal(new Bounds(0, -5, 25, 15), union);
    }

    [Fact]
    public void IntersectionCountsTouchingEdges()
    {
        var box = new Bounds(0, 0, 10, 10);

        Assert.True(box.Intersects(new Bounds(10, 0, 5, 5)));
        Assert.False(box.Intersects(new Bounds(10.5, 0, 5, 5)));
    }

    [Fact]
    public void ContainmentCountsTheBorder()
    {
        var box = new Bounds(0, 0, 10, 10);

        Assert.True(box.Contains(new Point(10, 10)));
        Assert.False(box.Contains(new Point(10.1, 5)));
    }

    [Fact]
    public void InflateGrowsEveryEdge()
    {
        Assert.Equal(new Bounds(-2, -2, 14, 14), new Bounds(0, 0, 10, 10).Inflate(2));
    }

    [Fact]
    public void ColourParsesHexWithOrWithoutHashInAnyCase()
    {
        Assert.Equal(new Rgb(0x1A, 0x2B, 0x3C), Rgb.Parse("#1A2b3C"));
        Assert.Equal(new Rgb(0x1A, 0x2B, 0x3C), Rgb.Parse("1a2b3c"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("1234567")]
    [InlineData("#gg0000")]
    public void ColourRejectsMalformedInput(string? hex)
    {
        Assert.False(Rgb.TryParse(hex, out _));
        Assert.Throws<FormatException>(() => Rgb.Parse(hex!));
    }

    [Fact]
    public void ColourHexRoundTripsEveryChannelValue()
    {
        for (var value = 0; value < 256; value++)
        {
            var rgb = new Rgb((byte)value, (byte)(255 - value), (byte)((value * 7) % 256));

            Assert.True(Rgb.TryParse(rgb.ToHex(), out var parsed));
            Assert.Equal(rgb, parsed);
        }
    }

    [Fact]
    public void ColourHexIsLowerCaseWithHash()
    {
        Assert.Equal("#0a1bff", new Rgb(10, 27, 255).ToHex());
    }
}
