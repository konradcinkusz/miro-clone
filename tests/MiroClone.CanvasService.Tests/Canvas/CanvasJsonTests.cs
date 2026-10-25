using System.Text.Json;
using MiroClone.CanvasService.Canvas;
using Xunit;

namespace MiroClone.CanvasService.Tests.Canvas;

/// <summary>The one JSON shape shared by REST, the hub and the snapshot, checked against the frozen contract's spelling.</summary>
public sealed class CanvasJsonTests
{
    [Fact]
    public void EveryLayerTypeRoundTripsThroughTheSharedOptions()
    {
        var board = BoardCanvas.Empty(Guid.NewGuid());
        var layers = new[]
        {
            new Layer(Guid.NewGuid(), LayerType.Rectangle, 1, 2, 3, 4, new Rgb(1, 2, 3)),
            new Layer(Guid.NewGuid(), LayerType.Ellipse, 1, 2, 3, 4, new Rgb(4, 5, 6)),
            new Layer(Guid.NewGuid(), LayerType.Text, 1, 2, 3, 4, new Rgb(7, 8, 9), Value: "hello"),
            new Layer(Guid.NewGuid(), LayerType.Note, 1, 2, 3, 4, new Rgb(10, 11, 12), Value: "note"),
            new Layer(Guid.NewGuid(), LayerType.Path, 0, 0, 5, 5, new Rgb(13, 14, 15), Points: [new PathPoint(1, 2), new PathPoint(3.5, 4)]),
        };
        foreach (var layer in layers)
        {
            board.Apply(new LayerMutation(MutationKind.Upsert, layer));
        }

        var json = JsonSerializer.Serialize(board.Snapshot(), CanvasJson.Options);
        var back = JsonSerializer.Deserialize<CanvasState>(json, CanvasJson.Options)!;

        Assert.Equal(board.Version, back.Version);
        Assert.Equal(board.Snapshot().LayerIds, back.LayerIds);
        foreach (var layer in layers)
        {
            // Compared by content: a record's own equality compares the points list by reference.
            Assert.True(LayerEquality.Same(layer, back.Layers[layer.Id]), $"Layer {layer.Id} did not survive the round trip.");
        }
    }

    [Fact]
    public void KeysAreCamelCaseEnumsAreNamedAndPathPointsArePairs()
    {
        var id = Guid.NewGuid();
        var state = new CanvasState(
            Version: 1,
            LayerIds: [id],
            Layers: new Dictionary<Guid, Layer>
            {
                [id] = new(id, LayerType.Path, 0, 0, 1, 1, new Rgb(0, 0, 0), Points: [new PathPoint(1, 2)]),
            });

        var json = JsonSerializer.Serialize(state, CanvasJson.Options);

        Assert.Contains("\"layerIds\"", json);
        Assert.Contains("\"layers\"", json);
        Assert.Contains("\"type\":\"Path\"", json);
        Assert.Contains("\"fill\":{\"r\":0,\"g\":0,\"b\":0}", json);
        Assert.Contains("\"points\":[[1,2]]", json);
        Assert.DoesNotContain("\"value\"", json);
    }

    [Fact]
    public void APathPointMustBeAPairOnTheWire()
    {
        var malformed = """{"version":1,"layerIds":[],"layers":{"00000000-0000-0000-0000-000000000001":{"id":"00000000-0000-0000-0000-000000000001","type":"Path","x":0,"y":0,"width":1,"height":1,"fill":{"r":0,"g":0,"b":0},"points":[[1,2,3]]}}}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CanvasState>(malformed, CanvasJson.Options));
    }
}
