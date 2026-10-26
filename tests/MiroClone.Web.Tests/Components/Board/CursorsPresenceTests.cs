using Bunit;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Realtime;
using MiroClone.Web.Components.Board;
using Xunit;

namespace MiroClone.Web.Tests.Components.Board;

/// <summary>Other participants' cursors and selections. Positions are world coordinates; a cursor keeps its size on screen at any zoom.</summary>
public sealed class CursorsPresenceTests : BunitContext
{
    private static readonly Guid Layer = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    [Fact]
    public void ARemoteCursorIsDrawnAtItsWorldPositionInTheRemoteParticipantsColour()
    {
        var remote = new RemoteParticipant(Guid.NewGuid(), "Member 2222", "#2563eb", new Point(40, 50), []);

        var cursors = Render<CursorsPresence>(parameters => parameters.Add(component => component.Remote, [remote]));

        var cursor = cursors.Find("[data-testid='remote-cursor']");
        Assert.Equal("translate(40 50) scale(1)", cursor.GetAttribute("transform"));
        Assert.Equal("Member 2222", cursors.Find(".board-cursor-name").TextContent);
        Assert.Equal("#2563eb", cursor.QuerySelector("path")!.GetAttribute("fill"));
    }

    [Fact]
    public void TheCursorKeepsItsSizeOnScreenWhenTheCanvasIsZoomed()
    {
        var remote = new RemoteParticipant(Guid.NewGuid(), "Member 2222", "#2563eb", new Point(0, 0), []);

        var cursors = Render<CursorsPresence>(parameters => parameters
            .Add(component => component.Remote, [remote])
            .Add(component => component.Zoom, 4.0));

        Assert.Equal("translate(0 0) scale(0.25)", cursors.Find("[data-testid='remote-cursor']").GetAttribute("transform"));
    }

    [Fact]
    public void ARemoteSelectionIsOutlinedWithoutACursor()
    {
        var box = new Bounds(10, 20, 30, 40);
        var remote = new RemoteParticipant(Guid.NewGuid(), "Member 2222", "#059669", null, [box]);

        var cursors = Render<CursorsPresence>(parameters => parameters.Add(component => component.Remote, [remote]));

        var outline = cursors.Find("[data-testid='remote-selection']");
        Assert.Equal("10", outline.GetAttribute("x"));
        Assert.Equal("40", outline.GetAttribute("height"));
        Assert.Equal("#059669", outline.GetAttribute("stroke"));
        Assert.Empty(cursors.FindAll("[data-testid='remote-cursor']"));
    }

    [Fact]
    public void WithNobodyElseOnTheBoardNothingIsDrawn()
    {
        var cursors = Render<CursorsPresence>(parameters => parameters.Add(component => component.Remote, []));

        Assert.Empty(cursors.Nodes);
    }
}
