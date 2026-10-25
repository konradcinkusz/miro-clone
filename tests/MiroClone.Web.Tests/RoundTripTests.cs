using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using MiroClone.CanvasService.Canvas;
using MiroClone.Web.Api;
using Xunit;
using ServiceBoardPage = MiroClone.BoardsService.Boards.BoardPage;
using ServiceBoardSummary = MiroClone.BoardsService.Boards.BoardSummary;
using ServiceCanvasState = MiroClone.CanvasService.Canvas.CanvasState;
using ServiceLayer = MiroClone.CanvasService.Canvas.Layer;
using ServiceLayerType = MiroClone.CanvasService.Canvas.LayerType;

namespace MiroClone.Web.Tests;

/// <summary>
/// The web app's DTO copies still match the services' serialization (blazor-web-app §5, §13). The boards copies are checked
/// against the boards service's own record types in both directions; the canvas copies against the frozen JSON in
/// docs/implementation-plan/README.md, because the canvas service's types arrive with WP05.
/// </summary>
public sealed class RoundTripTests
{
    // Minimal APIs serialize with the web defaults: camelCase, case-insensitive reads.
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    // The contract omits absent optional fields; the copy writes them as null unless told not to.
    private static readonly JsonSerializerOptions OmitNulls = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [Fact]
    public void ABoardPageSerializedByTheServiceReadsIntoTheCopy()
    {
        var id = Guid.Parse("7c1b0f0e-2f7a-4f8e-9d1a-0c8e5c7f2a11");
        var author = Guid.Parse("3d2f9c1e-8a4b-4c6d-b5e7-1f2a3b4c5d6e");
        var page = new ServiceBoardPage(
            [new ServiceBoardSummary(id, "Roadmap", author, "Ada", 4, new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero), true)],
            Total: 1);

        var json = JsonSerializer.Serialize(page, Wire);
        var copy = JsonSerializer.Deserialize<BoardPageDto>(json, Wire)!;

        Assert.Equal(1, copy.Total);
        var board = Assert.Single(copy.Items);
        Assert.Equal(new BoardSummaryDto(id, "Roadmap", author, "Ada", 4, new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero), true), board);
    }

    [Fact]
    public void ABoardPageCopiedHereReadsBackIntoTheServiceRecordUnchanged()
    {
        var copy = new BoardPageDto(
            [new BoardSummaryDto(Guid.NewGuid(), "Sketch", Guid.NewGuid(), "Grace", 9, DateTimeOffset.UnixEpoch, false)],
            Total: 1);

        var json = JsonSerializer.Serialize(copy, Wire);
        var service = JsonSerializer.Deserialize<ServiceBoardPage>(json, Wire)!;

        Assert.Equal(json, JsonSerializer.Serialize(service, Wire));
        Assert.Equal("Sketch", service.Items.Single().Title);
    }

    [Fact]
    public void ACanvasStateSerializedByTheCanvasServiceReadsIntoTheCopy()
    {
        var rectangle = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var stroke = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var state = new ServiceCanvasState(
            Version: 7,
            LayerIds: [rectangle, stroke],
            Layers: new Dictionary<Guid, ServiceLayer>
            {
                [rectangle] = new(rectangle, ServiceLayerType.Rectangle, 10, 20, 100, 50, new Rgb(255, 200, 0)),
                [stroke] = new(stroke, ServiceLayerType.Path, 0, 0, 5, 5, new Rgb(0, 0, 0), Value: null, Points: [new PathPoint(0, 0), new PathPoint(5, 5)]),
            });

        // The canvas endpoint writes with CanvasJson.Options: string enums, nulls omitted, path points as pairs.
        var json = JsonSerializer.Serialize(state, CanvasJson.Options);
        var copy = JsonSerializer.Deserialize<CanvasStateDto>(json, Wire)!;

        Assert.Contains("\"type\":\"Rectangle\"", json);
        Assert.Equal(7, copy.Version);
        Assert.Equal(new[] { rectangle, stroke }, copy.LayerIds);
        Assert.Equal(new FillDto(255, 200, 0), copy.Layers[rectangle].Fill);
        Assert.Equal(new double[] { 0, 0 }, copy.Layers[stroke].Points![0]);
        Assert.Equal(new double[] { 5, 5 }, copy.Layers[stroke].Points![1]);
    }

    [Fact]
    public void TheCanvasStateCopyMatchesTheFrozenContractShape()
    {
        const string contract = """
        {
          "version": 3,
          "layerIds": ["00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002"],
          "layers": {
            "00000000-0000-0000-0000-000000000001": {
              "id": "00000000-0000-0000-0000-000000000001", "type": "Rectangle",
              "x": 10, "y": 20, "width": 100, "height": 50, "fill": { "r": 255, "g": 200, "b": 0 }
            },
            "00000000-0000-0000-0000-000000000002": {
              "id": "00000000-0000-0000-0000-000000000002", "type": "Path",
              "x": 0, "y": 0, "width": 5, "height": 5, "fill": { "r": 0, "g": 0, "b": 0 },
              "points": [[0, 0], [5, 5]]
            }
          }
        }
        """;

        var state = JsonSerializer.Deserialize<CanvasStateDto>(contract, Wire)!;

        Assert.Equal(3, state.Version);
        Assert.Equal("Rectangle", state.Layers[state.LayerIds[0]].Type);
        Assert.Equal(new FillDto(255, 200, 0), state.Layers[state.LayerIds[0]].Fill);
        Assert.Null(state.Layers[state.LayerIds[0]].Points);
        Assert.Equal(2, state.Layers[state.LayerIds[1]].Points!.Count);
        // The contract omits absent optional fields; the copy writes them as null unless told not to.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(contract), JsonNode.Parse(JsonSerializer.Serialize(state, OmitNulls))));
    }
}
