using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using MiroClone.Web.Api;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Realtime;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Realtime;

/// <summary>
/// The live session over a fake hub: connect and re-read, the outbox, remote edits through MutationApplier, refusals and resync,
/// reconnect, presence pacing and the participants list, disposal, and the dispatcher. The two-session test on the real hub is
/// in <see cref="CanvasHubConvergenceTests"/>.
/// </summary>
public sealed class HubCanvasSessionTests
{
    private static readonly Guid Board = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Self = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid C = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000003");

    private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task ItConnectsJoinsAndTakesTheStateTheServerSends()
    {
        var server = new FakeHubConnector { ServerState = WireFixtures.State(1, WireFixtures.Rectangle(A)) };
        await using var session = Open(server, retry: Quick);

        session.Start();

        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        Assert.Equal([A], session.Document.Order);
        Assert.Equal(1, server.Latest.Joins);
    }

    [Fact]
    public async Task ALocalEditIsSentOnceInOrderAndAppliedAtOnce()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");

        var first = Layer(A);
        var second = Layer(B);
        var applied = session.Commit([LayerMutation.Upsert(first)]);
        session.Commit([LayerMutation.Upsert(second)]);

        Assert.Single(applied);
        Assert.Equal([A, B], session.Document.Order);
        await Eventually.AssertAsync(() => server.Latest.Sent.Count == 2, "both edits to be sent");
        Assert.Equal(["Upsert", "Upsert"], server.Latest.Sent.Select(sent => sent.Kind));
        Assert.Equal([A, B], server.Latest.Sent.Select(sent => sent.Layer!.Id));
    }

    [Fact]
    public async Task ARemoteEditIsAppliedThroughTheMutationApplierAndSaysSo()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        var changes = 0;
        session.Changed += () => changes++;
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");

        server.Latest.Listener.OnLayerMutation("peer", WireFixtures.Upsert(WireFixtures.Rectangle(B)));

        Assert.Equal([B], session.Document.Order);
        Assert.True(changes >= 1);
        Assert.Empty(server.Latest.Sent);
    }

    [Fact]
    public async Task AMalformedRemoteEditChangesNothingAndSaysNothing()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        var changes = 0;
        session.Changed += () => changes++;

        // A negative width is refused by MutationApplier; an unknown kind fails the shape check before it.
        server.Latest.Listener.OnLayerMutation("peer", WireFixtures.Upsert(WireFixtures.Rectangle(B, width: -1)));
        server.Latest.Listener.OnLayerMutation("peer", new LayerMutationDto("Shuffle", null, null, null));

        Assert.Empty(session.Document.Order);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task ARemoteDeleteRemovesTheLayer()
    {
        var server = new FakeHubConnector { ServerState = WireFixtures.State(1, WireFixtures.Rectangle(A)) };
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");

        server.Latest.Listener.OnLayerMutation("peer", WireFixtures.Delete(A));

        Assert.Empty(session.Document.Order);
    }

    [Fact]
    public async Task AfterAReconnectTheStateIsReReadAndEditsMadeWhileDownAreReplayedAndSent()
    {
        // A retry delay long enough that the edit below is made while the connection is down.
        var server = new FakeHubConnector { ServerState = WireFixtures.State(1, WireFixtures.Rectangle(A)) };
        await using var session = Open(server, retry: TimeSpan.FromMilliseconds(300));
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the first connection");
        var dropped = server.Latest;

        dropped.Drop(new IOException("the network went away"));
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Reconnecting, "the reconnecting state");
        Assert.True(dropped.Disposed);

        // While it was down, the room changed without this client, and this client edited locally.
        server.ServerState = WireFixtures.State(2, WireFixtures.Rectangle(A), WireFixtures.Rectangle(C));
        session.Commit([LayerMutation.Upsert(Layer(B))]);
        Assert.Equal([A, B], session.Document.Order);

        await Eventually.AssertAsync(() => server.Connections.Count == 2 && session.Connection == CanvasConnectionState.Live, "a second live connection");
        var reconnected = server.Latest;
        await Eventually.AssertAsync(() => reconnected.Sent.Any(sent => sent.Layer?.Id == B), "the edit made while down to be sent");
        Assert.Equal(1, reconnected.Joins);
        Assert.Equal([A, C, B], session.Document.Order);
    }

    [Fact]
    public async Task AReconnectingSessionSaysSoBeforeItTriesAgain()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: TimeSpan.FromMinutes(1));
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the first connection");

        server.Latest.Drop(new IOException("gone"));

        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Reconnecting, "the reconnecting state");
    }

    [Fact]
    public async Task AServiceRefusalDropsTheEditAndReReadsTheBoard()
    {
        var server = new FakeHubConnector { ServerState = WireFixtures.State(1, WireFixtures.Rectangle(A)) };
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        server.Latest.SendFailures.Enqueue(new HubException("layer_limit"));

        session.Commit([LayerMutation.Upsert(Layer(B))]);

        await Eventually.AssertAsync(() => server.Latest.Joins == 2, "a re-read after the refusal");
        await Eventually.AssertAsync(() => session.Document.Order.SequenceEqual([A]), "the service's state to win");
        Assert.Empty(server.Latest.Sent);
    }

    [Fact]
    public async Task ARateLimitedEditIsKeptAndSentLater()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        server.Latest.SendFailures.Enqueue(new HubException("rate_limited"));

        session.Commit([LayerMutation.Upsert(Layer(A))]);

        await Eventually.AssertAsync(() => server.Latest.Sent.Count == 1, "the kept edit to be sent after the pause");
        Assert.Equal(1, server.Latest.Joins);
    }

    [Fact]
    public async Task ABoardTheCallerCannotSeeEndsTheSessionAsUnavailable()
    {
        var server = new FakeHubConnector { JoinError = new HubException("board_not_found") };
        await using var session = Open(server, retry: Quick);
        session.Start();

        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Unavailable, "the unavailable state");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(server.Connections);
    }

    [Fact]
    public async Task AFailedJoinIsRetriedWithBackoffUntilItWorks()
    {
        var server = new FakeHubConnector { StartError = new IOException("unreachable") };
        await using var session = Open(server, retry: Quick);
        session.Start();

        await Eventually.AssertAsync(() => server.Connections.Count >= 3, "several attempts");
        Assert.Equal(CanvasConnectionState.Reconnecting, session.Connection);

        server.StartError = null;
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "a live connection once the service answers");
    }

    [Fact]
    public async Task WithoutASignInTheSessionDoesNotConnectAndSaysUnavailable()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick, token: _ => Task.FromResult<string?>(null));
        session.Start();

        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Unavailable, "the unavailable state");
        Assert.Empty(server.Connections);
    }

    [Fact]
    public async Task PresenceIsSentNoFasterThanTwentyTimesASecondAndTheLastStateWins()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        var connection = server.Latest;

        var clock = Stopwatch.StartNew();
        for (var step = 0; step < 300; step++)
        {
            session.SetPresence(new Point(step, step), []);
            await Task.Delay(3, TestContext.Current.CancellationToken);
        }

        var seconds = clock.Elapsed.TotalSeconds;
        Assert.True(connection.Presence.Count <= (seconds * 20) + 2, $"{connection.Presence.Count} sends in {seconds:F2} s");
        await Eventually.AssertAsync(() => connection.Presence.Count > 0 && connection.Presence[^1].Cursor == new CursorDto(299, 299), "the last cursor to be sent");
    }

    [Fact]
    public async Task ThePresenceSentIsTheCursorAndSelectionWithTheServiceToStampTheIdentity()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");

        session.SetPresence(new Point(4, 5), [A]);

        await Eventually.AssertAsync(() => server.Latest.Presence.Count == 1, "one presence send");
        var sent = server.Latest.Presence[0];
        Assert.Equal(new CursorDto(4, 5), sent.Cursor);
        Assert.Equal([A], sent.Selection);
        Assert.Equal(Self, sent.UserId);
    }

    [Fact]
    public async Task EachUserIsListedOnceWithTheSignedInUserFirst()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");

        server.Latest.Listener.OnPresenceUpdated("tab-1", WireFixtures.Presence(Other, "Member 2222"));
        server.Latest.Listener.OnPresenceUpdated("tab-2", WireFixtures.Presence(Other, "Member 2222"));
        server.Latest.Listener.OnPresenceUpdated("tab-3", WireFixtures.Presence(Self, "Member 1111"));

        var participants = session.Participants;
        Assert.Equal([Self, Other], participants.Select(person => person.UserId));
        Assert.True(participants[0].IsSelf);
        Assert.Equal("Owner", participants[0].Name);
        Assert.Equal([Other], session.RemoteParticipants.Select(remote => remote.UserId));
    }

    [Fact]
    public async Task ARemoteParticipantCarriesTheBoundsOfTheLayersTheyHaveSelected()
    {
        var server = new FakeHubConnector { ServerState = WireFixtures.State(1, WireFixtures.Rectangle(A, x: 5, y: 6, width: 7, height: 8)) };
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");

        server.Latest.Listener.OnPresenceUpdated("tab-2", WireFixtures.Presence(Other, "Member 2222", "#059669", new CursorDto(1, 2), A));

        var remote = Assert.Single(session.RemoteParticipants);
        Assert.Equal(new Point(1, 2), remote.Cursor);
        Assert.Equal("#059669", remote.Color);
        Assert.Equal([new Bounds(5, 6, 7, 8)], remote.Selection);
    }

    [Fact]
    public async Task AMalformedColourFromTheServiceFallsBackToTheNeutralColour()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");

        server.Latest.Listener.OnPresenceUpdated("tab-2", WireFixtures.Presence(Other, colour: "red; background:url(x)"));

        Assert.Equal(HubWire.FallbackColour, Assert.Single(session.RemoteParticipants).Color);
    }

    [Fact]
    public async Task APresenceLeaveForAKnownConnectionRemovesOnlyThatTab()
    {
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        server.Latest.Listener.OnPresenceUpdated("tab-1", WireFixtures.Presence(Other));
        server.Latest.Listener.OnPresenceUpdated("tab-2", WireFixtures.Presence(Self));

        server.Latest.Listener.OnPresenceLeft("tab-1");

        Assert.Equal([Self], session.Participants.Select(person => person.UserId));
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(1, server.Latest.Joins);
    }

    [Fact]
    public async Task APresenceLeaveForAConnectionThisSessionDoesNotKnowReReadsTheBoard()
    {
        var joinedBefore = new FakeHubConnector
        {
            ServerState = WireFixtures.State(1),
            ServerPresence = [WireFixtures.Presence(Other, "Member 2222")],
        };
        await using var session = Open(joinedBefore, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        Assert.Single(session.RemoteParticipants);

        // The other person was on the board before this session joined, so the service never named their connection.
        joinedBefore.Latest.Listener.OnPresenceLeft("unknown-connection");

        await Eventually.AssertAsync(() => joinedBefore.Latest.Joins == 2, "the board to be re-read");
    }

    [Fact]
    public async Task DisposingClosesTheConnectionAndSendsNothingMore()
    {
        var server = new FakeHubConnector();
        var session = Open(server, retry: Quick);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        var connection = server.Latest;

        await session.DisposeAsync();
        session.Commit([LayerMutation.Upsert(Layer(A))]);
        session.SetPresence(new Point(1, 1), []);
        await Task.Delay(80, TestContext.Current.CancellationToken);

        Assert.True(connection.Disposed);
        Assert.Empty(connection.Sent);
        Assert.Empty(connection.Presence);
        Assert.Single(server.Connections);
    }

    [Fact]
    public async Task DocumentChangesRunOnTheDispatcherNotOnTheThreadThatDeliveredThem()
    {
        var dispatcher = new QueueingContext();
        var server = new FakeHubConnector();
        await using var session = Open(server, retry: Quick, dispatcher: dispatcher);
        session.Start();
        await Eventually.AssertAsync(() => session.Connection == CanvasConnectionState.Live, "the session to be live");
        await Task.Delay(20, TestContext.Current.CancellationToken);
        dispatcher.Pump();
        var changes = 0;
        session.Changed += () => changes++;

        // The transport delivers on this thread. Nothing has changed yet: the work is queued for the dispatcher.
        server.Latest.Listener.OnLayerMutation("peer", WireFixtures.Upsert(WireFixtures.Rectangle(B)));
        Assert.Empty(session.Document.Order);
        Assert.True(dispatcher.Queued > 0);

        dispatcher.Pump();

        Assert.Equal([B], session.Document.Order);
        Assert.Equal(1, changes);
    }

    private static HubCanvasSession Open(
        FakeHubConnector server,
        TimeSpan retry,
        Func<CancellationToken, Task<string?>>? token = null,
        SynchronizationContext? dispatcher = null) =>
        new(
            Board,
            Self,
            "Owner",
            new CanvasDocument(),
            server,
            token ?? (_ => Task.FromResult<string?>("token")),
            TimeProvider.System,
            dispatcher,
            logger: null,
            retryDelay: retry);

    private static Layer Layer(Guid id) => new(id, LayerType.Rectangle, 0, 0, 10, 10, new Rgb(1, 2, 3));
}
