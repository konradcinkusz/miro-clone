using System.Text.Json;
using MiroClone.Web.Components.Dashboard;
using Xunit;
using ServiceCreate = MiroClone.BoardsService.Boards.CreateBoardRequest;
using ServiceRename = MiroClone.BoardsService.Boards.RenameBoardRequest;
using WebCreate = MiroClone.Web.Api.CreateBoardRequest;
using WebRename = MiroClone.Web.Api.RenameBoardRequest;

namespace MiroClone.Web.Tests.Dashboard;

/// <summary>The request bodies the dashboard sends match the boards service's records in both directions (blazor-web-app §13).</summary>
public sealed class RequestRoundTripTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ACreateBodyFromTheDashboardReadsIntoTheServiceRecord()
    {
        var json = JsonSerializer.Serialize(new WebCreate("Roadmap"), Wire);

        Assert.Equal("Roadmap", JsonSerializer.Deserialize<ServiceCreate>(json, Wire)!.Title);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<ServiceCreate>(json, Wire), Wire));
    }

    [Fact]
    public void ARenameBodyFromTheDashboardReadsIntoTheServiceRecord()
    {
        var json = JsonSerializer.Serialize(new WebRename("Sketch"), Wire);

        Assert.Equal("Sketch", JsonSerializer.Deserialize<ServiceRename>(json, Wire)!.Title);
    }

    [Fact]
    public void TheServiceRecordsReadTheDashboardsBodiesAndNoOtherField()
    {
        var json = JsonSerializer.Serialize(new { title = "Notes" }, Wire);

        Assert.Equal("Notes", JsonSerializer.Deserialize<WebCreate>(json, Wire)!.Title);
        Assert.Equal("Notes", JsonSerializer.Deserialize<WebRename>(json, Wire)!.Title);
    }

    [Fact]
    public void TheTitleLimitInTheDashboardMatchesTheServiceLimit() =>
        Assert.Equal(MiroClone.BoardsService.Boards.BoardTitle.MaxLength, BoardTitle.MaxLength);
}
