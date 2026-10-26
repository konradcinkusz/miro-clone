using System.Net;
using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.Web.Api;
using MiroClone.Web.Auth;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Interaction;
using MiroClone.Web.Canvas.Interop;
using MiroClone.Web.Components.Pages.Board;
using MiroClone.Web.Canvas.Realtime;
using MiroClone.Web.Tests.Canvas.Realtime;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests.Pages.Board;

/// <summary>
/// The board page: the load states, the title, and what the page does with input. Every local edit stays in the session, so
/// the only network traffic is the two reads on load.
/// </summary>
public sealed class BoardPageTests : BunitContext
{
    private static readonly Guid Board = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Rectangle = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private readonly ScriptedHandler _canvas = new();
    private readonly ScriptedHandler _boards = new();
    private readonly FakeCanvasPointer _pointer = new();
    private readonly FakeHubConnector _hub = new();

    public BoardPageTests()
    {
        Services.AddSingleton<ICurrentSession>(new FakeSession(Principal()));
        Services.AddSingleton<ICanvasPointerSource>(_pointer);
        Services.AddSingleton<ICanvasSessionFactory>(new TestSessionFactory(_hub));
        Services.AddSingleton(new CanvasClient(new HttpClient(_canvas) { BaseAddress = new Uri("http://canvas-service/") }));
        Services.AddSingleton(new BoardsClient(new HttpClient(_boards) { BaseAddress = new Uri("http://boards-service/") }));
    }

    [Fact]
    public void ABoardLoadsItsTitleAndLayersAndWritesNothing()
    {
        ArrangeBoard("Roadmap", [(Rectangle, "Rectangle")]);

        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='canvas-surface']")));
        Assert.Equal("Roadmap", page.Find("[data-testid='board-title']").TextContent);
        Assert.Single(page.FindAll("[data-testid='layer-rectangle']"));
        Assert.Equal("O", page.Find("[data-testid='participant-self']").TextContent);
        Assert.Equal("true", page.Find("[data-testid='tool-select']").GetAttribute("aria-pressed"));
        Assert.All(_canvas.Calls, call => Assert.Equal("GET", call.Method));
        Assert.Equal(1, _canvas.CountCalls("GET", $"api/canvas/{Board:D}"));
    }

    [Fact]
    public async Task ADrawnRectangleAppearsLocallyAndTheToolReturnsToSelect()
    {
        ArrangeBoard("Roadmap", []);
        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='canvas-surface']")));

        page.Find("[data-testid='tool-rectangle']").Click();
        await _pointer.Emit(new PointerInput(PointerPhase.Down, new Point(10, 10), Primary: true, Additive: false));
        await _pointer.Emit(new PointerInput(PointerPhase.Up, new Point(90, 60), Primary: true, Additive: false));

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid='layer-rectangle']")));
        Assert.Equal("true", page.Find("[data-testid='tool-select']").GetAttribute("aria-pressed"));
        Assert.All(_canvas.Calls, call => Assert.Equal("GET", call.Method));
    }

    [Fact]
    public async Task ReachingTheLayerLimitSaysSoOnThePage()
    {
        ArrangeBoard("Full", Enumerable.Range(0, 100).Select(_ => (Guid.NewGuid(), "Rectangle")).ToArray());
        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='canvas-surface']")));

        page.Find("[data-testid='tool-rectangle']").Click();
        await _pointer.Emit(new PointerInput(PointerPhase.Down, new Point(0, 0), Primary: true, Additive: false));

        page.WaitForAssertion(() => Assert.Equal(BoardEditor.LimitNotice, page.Find("[data-testid='board-notice']").TextContent));
    }

    [Fact]
    public void ACanvasThatAnswers404ShowsTheNotFoundStateAndNoAlert()
    {
        _canvas.On(HttpMethod.Get, $"api/canvas/{Board:D}", HttpStatusCode.NotFound);

        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='board-not-found']")));
        Assert.Empty(page.FindAll("[role='alert']"));
        Assert.Empty(page.FindAll("[data-testid='canvas-surface']"));
    }

    [Fact]
    public void ABoardMissingFromTheWorkspaceListIsNotFound()
    {
        ArrangeBoard("Roadmap", [], listed: false);

        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='board-not-found']")));
    }

    [Fact]
    public void AFailedCanvasLoadShowsOneAlertAndNoCanvas()
    {
        _canvas.On(HttpMethod.Get, $"api/canvas/{Board:D}", HttpStatusCode.InternalServerError, new { error = "boom" });
        ArrangeList("Roadmap");

        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[role='alert']")));
        Assert.Contains("could not be loaded", page.Find("[role='alert']").TextContent);
        Assert.Empty(page.FindAll("[data-testid='canvas-surface']"));
    }

    [Fact]
    public void AMalformedCanvasStateIsAFailedLoadNotACrash()
    {
        _canvas.On(HttpMethod.Get, $"api/canvas/{Board:D}", HttpStatusCode.OK, new
        {
            version = 1,
            layerIds = new[] { Rectangle },
            layers = new Dictionary<string, object>(),
        });
        ArrangeList("Roadmap");

        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[role='alert']")));
    }

    [Fact]
    public async Task DisposingTheBoardDisposesTheBrowserSource()
    {
        ArrangeBoard("Roadmap", []);
        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='canvas-surface']")));

        await DisposeAsync();

        Assert.True(_pointer.Disposed);
    }

    [Fact]
    public void ALostConnectionSaysSoAndKeepsTheBoardOnScreen()
    {
        ArrangeBoard("Roadmap", [(Rectangle, "Rectangle")]);
        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='canvas-surface']")));
        page.WaitForAssertion(() => Assert.Single(_hub.Connections));

        _hub.Latest.Drop(new IOException("the network went away"));

        page.WaitForAssertion(() => Assert.Equal("reconnecting", page.Find("[data-testid='connection-banner']").GetAttribute("data-state")));
        Assert.Single(page.FindAll("[data-testid='layer-rectangle']"));
    }

    [Fact]
    public void ARemoteDeleteRemovesTheLayerFromTheBoard()
    {
        ArrangeBoard("Roadmap", [(Rectangle, "Rectangle")]);
        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid='layer-rectangle']")));
        page.WaitForAssertion(() => Assert.Single(_hub.Connections));

        _hub.Latest.Listener.OnLayerMutation("peer", WireFixtures.Delete(Rectangle));

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid='layer-rectangle']")));
    }

    [Fact]
    public void AParticipantWhoJoinsAppearsInTheHeaderOnce()
    {
        ArrangeBoard("Roadmap", []);
        var page = Render<BoardPage>(parameters => parameters.Add(component => component.BoardId, Board));
        page.WaitForAssertion(() => Assert.Single(_hub.Connections));

        _hub.Latest.Listener.OnPresenceUpdated("tab-1", WireFixtures.Presence(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Member 2222"));
        _hub.Latest.Listener.OnPresenceUpdated("tab-2", WireFixtures.Presence(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Member 2222"));

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid='participant']")));
        Assert.Equal("Member 2222", page.Find("[data-testid='participant']").GetAttribute("title"));
    }

    /// <summary>Arranges a board the workspace lists, with the given layers on its canvas.</summary>
    private void ArrangeBoard(string title, (Guid Id, string Type)[] layers, bool listed = true)
    {
        ArrangeCanvas(layers);
        ArrangeList(title, listed);
    }

    private void ArrangeList(string title, bool listed = true) =>
        _boards.On(HttpMethod.Get, "api/boards", HttpStatusCode.OK, new
        {
            items = listed
                ? new[] { new { id = Board, title, authorId = Guid.NewGuid(), authorName = "Owner", imageSeed = 1, createdAt = DateTimeOffset.UnixEpoch, isFavourite = false } }
                : Array.Empty<object>(),
            total = listed ? 1 : 0,
        });

    private void ArrangeCanvas((Guid Id, string Type)[] layers) =>
        _canvas.On(HttpMethod.Get, $"api/canvas/{Board:D}", HttpStatusCode.OK, new
        {
            version = 1,
            layerIds = layers.Select(layer => layer.Id).ToArray(),
            layers = layers.ToDictionary(
                layer => layer.Id,
                layer => (object)new
                {
                    id = layer.Id,
                    type = layer.Type,
                    x = 10.0,
                    y = 20.0,
                    width = 100.0,
                    height = 50.0,
                    fill = new { r = 1, g = 2, b = 3 },
                }),
        });

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity([new Claim("sub", "11111111-1111-1111-1111-111111111111"), new Claim("email", "owner@dev.test")], "test", "email", null));

    private sealed class FakeSession(ClaimsPrincipal principal) : ICurrentSession
    {
        public Task<string?> IdAsync() => Task.FromResult<string?>("session-1");

        public Task<ClaimsPrincipal?> PrincipalAsync() => Task.FromResult<ClaimsPrincipal?>(principal);
    }
}
