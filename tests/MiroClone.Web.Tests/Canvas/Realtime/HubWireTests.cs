using System.Text.Json;
using MiroClone.Web.Api;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Realtime;
using Xunit;
using Service = MiroClone.CanvasService.Canvas;

namespace MiroClone.Web.Tests.Canvas.Realtime;

/// <summary>
/// The hub's JSON, read both ways between the service's own types and the Web's copies (blazor-web-app §5: DTOs are copies, pinned
/// by round-trip tests). The service's <c>CanvasJson</c> is the reference, so a renamed field or enum name fails here.
/// </summary>
public sealed class HubWireTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid LayerId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    [Fact]
    public void AServicePresenceReadsAsTheClientCopy()
    {
        var presence = new Service.Presence(UserId, "Member 1111", "#2563eb", new Service.Point(1.5, 2), [LayerId]);

        var copy = WriteThenRead<PresenceDto>(Service.CanvasJson.Options, WebJson, presence);

        Assert.Equal(UserId, copy.UserId);
        Assert.Equal("Member 1111", copy.Name);
        Assert.Equal("#2563eb", copy.Color);
        Assert.Equal(new CursorDto(1.5, 2), copy.Cursor);
        Assert.Equal([LayerId], copy.Selection);
    }

    [Fact]
    public void AClientPresenceWithoutACursorReadsAsTheServiceExpects()
    {
        var sent = new PresenceDto(UserId, string.Empty, string.Empty, null, []);

        var read = WriteThenRead<Service.Presence>(WebJson, Service.CanvasJson.Options, sent);

        Assert.Null(read.Cursor);
        Assert.Empty(read.Selection);
    }

    [Fact]
    public void AClientUpsertWithPointsReadsAsTheServiceLayer()
    {
        var layer = new Layer(LayerId, LayerType.Path, 1, 2, 30, 40, new Rgb(9, 8, 7), Value: null, Points: [new Point(1, 2), new Point(3.5, 4)]);
        var sent = HubWire.ToWire(LayerMutation.Upsert(layer));

        var read = WriteThenRead<Service.LayerMutation>(WebJson, Service.CanvasJson.Options, sent);

        Assert.Equal(Service.MutationKind.Upsert, read.Kind);
        Assert.NotNull(read.Layer);
        Assert.Equal(LayerId, read.Layer.Id);
        Assert.Equal(Service.LayerType.Path, read.Layer.Type);
        Assert.Equal(new Service.Rgb(9, 8, 7), read.Layer.Fill);
        Assert.Equal([new Service.PathPoint(1, 2), new Service.PathPoint(3.5, 4)], read.Layer.Points);
    }

    [Fact]
    public void AServiceDeleteAndReorderReadAsTheClientMutations()
    {
        var delete = new Service.LayerMutation(Service.MutationKind.Delete, Id: LayerId);
        var reorder = new Service.LayerMutation(Service.MutationKind.Reorder, Order: [LayerId]);

        var deleted = HubWire.ToCore(WriteThenRead<LayerMutationDto>(Service.CanvasJson.Options, WebJson, delete));
        var reordered = HubWire.ToCore(WriteThenRead<LayerMutationDto>(Service.CanvasJson.Options, WebJson, reorder));

        Assert.Equal(LayerMutationKind.Delete, deleted.Kind);
        Assert.Equal(LayerId, deleted.Id);
        Assert.Equal(LayerMutationKind.Reorder, reordered.Kind);
        Assert.Equal([LayerId], reordered.Order);
    }

    [Fact]
    public void AServiceStateReadsAsTheClientCopy()
    {
        var layers = new Dictionary<Guid, Service.Layer>
        {
            [LayerId] = new(LayerId, Service.LayerType.Rectangle, 1, 2, 3, 4, new Service.Rgb(5, 6, 7)),
        };
        var state = new Service.CanvasState(7, [LayerId], layers);

        var copy = WriteThenRead<CanvasStateDto>(Service.CanvasJson.Options, WebJson, state);

        Assert.Equal(7, copy.Version);
        Assert.Equal([LayerId], copy.LayerIds);
        Assert.Equal("Rectangle", copy.Layers[LayerId].Type);
        Assert.Equal(new FillDto(5, 6, 7), copy.Layers[LayerId].Fill);
    }

    [Fact]
    public void ANonHexColourIsNotSentToTheBrowser()
    {
        Assert.Equal(HubWire.FallbackColour, HubWire.Colour("javascript:alert(1)"));
        Assert.Equal(HubWire.FallbackColour, HubWire.Colour(null));
        Assert.Equal("#e11d48", HubWire.Colour("#e11d48"));
    }

    [Fact]
    public void AMutationWithoutItsFieldIsMalformed()
    {
        Assert.Throws<FormatException>(() => HubWire.ToCore(new LayerMutationDto("Upsert", null, null, null)));
        Assert.Throws<FormatException>(() => HubWire.ToCore(new LayerMutationDto("Delete", null, null, null)));
        Assert.Throws<FormatException>(() => HubWire.ToCore(new LayerMutationDto("Reorder", null, null, null)));
        Assert.Throws<FormatException>(() => HubWire.ToCore(new LayerMutationDto("Explode", null, null, null)));
    }

    /// <summary>Writes <paramref name="value"/> with <paramref name="writer"/> and reads the JSON back as <typeparamref name="T"/> with <paramref name="reader"/>.</summary>
    private static T WriteThenRead<T>(JsonSerializerOptions writer, JsonSerializerOptions reader, object value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, writer), reader)
        ?? throw new InvalidOperationException("The JSON was null.");
}
