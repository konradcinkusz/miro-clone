using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using MiroClone.CanvasService.Realtime;
using Xunit;

namespace MiroClone.CanvasService.Tests.Realtime;

/// <summary>
/// The hub on TestServer with real SignalR clients over long polling, so no socket is opened. Covers convergence between
/// clients, membership refusals, identity stamping, the layer cap, degraded editing and the rate limit.
/// </summary>
public sealed class CanvasHubTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Guid _board = Guid.NewGuid();
    private readonly List<HubConnection> _connections = [];
    private readonly Dictionary<HubConnection, Guid> _usersByConnection = [];
    private WebApplication _app = null!;
    private FakeBoardAccess _boards = null!;
    private FakeSnapshotStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await ServiceHost.StartAsync(new Dictionary<string, string?>());
        _boards = _app.Services.GetRequiredService<FakeBoardAccess>();
        _store = _app.Services.GetRequiredService<FakeSnapshotStore>();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task TwoClientsOnTheSameBoardSeeEachOthersMutations()
    {
        var ada = await MemberAsync();
        var grace = await MemberAsync();
        await JoinAsync(ada);
        await JoinAsync(grace);
        var received = ListenFor<LayerMutation>(grace, CanvasHubMessages.LayerMutation);
        var layer = Rectangle();

        await ada.InvokeAsync("SendMutation", _board, new LayerMutation(MutationKind.Upsert, layer), TestContext.Current.CancellationToken);

        var mutation = await received.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(MutationKind.Upsert, mutation.Kind);
        Assert.Equal(layer.Id, mutation.Layer!.Id);
    }

    [Fact]
    public async Task ALateJoinerIsSentTheBoardAsItStandsAndTheParticipants()
    {
        var ada = await MemberAsync();
        await JoinAsync(ada);
        var layer = Rectangle();
        await ada.InvokeAsync("SendMutation", _board, new LayerMutation(MutationKind.Upsert, layer), TestContext.Current.CancellationToken);
        var grace = await MemberAsync();
        var initial = new TaskCompletionSource<(CanvasState State, IReadOnlyList<Presence> Participants)>(TaskCreationOptions.RunContinuationsAsynchronously);
        grace.On<CanvasState, IReadOnlyList<Presence>>(CanvasHubMessages.InitialState, (state, participants) => initial.TrySetResult((state, participants)));

        await JoinAsync(grace);

        var (state, participants) = await initial.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal([layer.Id], state.LayerIds);
        Assert.Equal(2, participants.Count);
    }

    [Fact]
    public async Task ANonMemberIsRefusedWithBoardNotFound()
    {
        var outsider = await ConnectAsync(Guid.NewGuid());

        var error = await Assert.ThrowsAsync<HubException>(
            () => outsider.InvokeAsync("JoinBoard", _board, TestContext.Current.CancellationToken));

        Assert.Contains("board_not_found", error.Message);
    }

    [Fact]
    public async Task AMutationBeforeJoiningIsRefused()
    {
        var ada = await MemberAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => ada.InvokeAsync(
            "SendMutation", _board, new LayerMutation(MutationKind.Upsert, Rectangle()), TestContext.Current.CancellationToken));

        Assert.Contains("not_joined", error.Message);
    }

    [Fact]
    public async Task AMalformedLayerIsRefusedWithAnErrorCode()
    {
        var ada = await MemberAsync();
        await JoinAsync(ada);

        var error = await Assert.ThrowsAsync<HubException>(() => ada.InvokeAsync(
            "SendMutation", _board, new LayerMutation(MutationKind.Upsert, Rectangle() with { Width = -1 }), TestContext.Current.CancellationToken));

        Assert.Contains("invalid_layer", error.Message);
    }

    [Fact]
    public async Task TheHundredAndFirstLayerIsRefusedWithTheLayerLimitCode()
    {
        var ada = await MemberAsync();
        await JoinAsync(ada);
        for (var index = 0; index < LayerRules.MaxLayers; index++)
        {
            await ada.InvokeAsync("SendMutation", _board, new LayerMutation(MutationKind.Upsert, Rectangle()), TestContext.Current.CancellationToken);
        }

        var error = await Assert.ThrowsAsync<HubException>(() => ada.InvokeAsync(
            "SendMutation", _board, new LayerMutation(MutationKind.Upsert, Rectangle()), TestContext.Current.CancellationToken));

        Assert.Contains("layer_limit", error.Message);
    }

    [Fact]
    public async Task PresenceIdentityIsStampedFromTheTokenNotTheClient()
    {
        var ada = await MemberAsync("Ada");
        var grace = await MemberAsync("Grace");
        await JoinAsync(grace);
        var seen = ListenFor<Presence>(grace, CanvasHubMessages.PresenceUpdated, presence => presence.Cursor == new Point(1, 2));
        await JoinAsync(ada);

        await ada.InvokeAsync(
            "UpdatePresence",
            _board,
            new Presence(Guid.NewGuid(), "Mallory", "#000000", new Point(1, 2), []),
            TestContext.Current.CancellationToken);

        var presence = await seen.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(_usersByConnection[ada], presence.UserId);
        Assert.Equal("Ada", presence.Name);
        Assert.NotEqual("#000000", presence.Color);
    }

    [Fact]
    public async Task ADegradedStoreStillBroadcastsEditsAndWritesThemWhenItRecovers()
    {
        var ada = await MemberAsync();
        var grace = await MemberAsync();
        await JoinAsync(ada);
        await JoinAsync(grace);
        var received = ListenFor<LayerMutation>(grace, CanvasHubMessages.LayerMutation);
        var layer = Rectangle();
        _store.Offline = true;
        var registry = _app.Services.GetRequiredService<CanvasRegistry>();

        await ada.InvokeAsync("SendMutation", _board, new LayerMutation(MutationKind.Upsert, layer), TestContext.Current.CancellationToken);
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(layer.Id, (await received.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Layer!.Id);
        Assert.False(registry.IsPersisting);
        Assert.Null(_store.Row(_board));

        _store.Offline = false;
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(registry.IsPersisting);
        Assert.Equal(1, _store.Row(_board)!.Version);
    }

    [Fact]
    public async Task APresenceBurstIsCutOffByTheRateLimit()
    {
        var ada = await MemberAsync();
        await JoinAsync(ada);
        var rejected = 0;

        for (var index = 0; index < 100; index++)
        {
            try
            {
                await ada.InvokeAsync(
                    "UpdatePresence",
                    _board,
                    new Presence(Guid.Empty, string.Empty, string.Empty, null, []),
                    TestContext.Current.CancellationToken);
            }
            catch (HubException error) when (error.Message.Contains("rate_limited", StringComparison.Ordinal))
            {
                rejected++;
            }
        }

        Assert.True(rejected > 0);
    }

    [Fact]
    public async Task ABoardsServiceBeingDownRefusesTheJoinWithUnavailable()
    {
        _boards.Unavailable = true;
        var ada = await MemberAsync();

        var error = await Assert.ThrowsAsync<HubException>(
            () => ada.InvokeAsync("JoinBoard", _board, TestContext.Current.CancellationToken));

        Assert.Contains("canvas_unavailable", error.Message);
    }

    private static Layer Rectangle() => new(Guid.NewGuid(), LayerType.Rectangle, 0, 0, 10, 10, new Rgb(37, 99, 235));

    /// <summary>A connected caller whose user is granted the test board.</summary>
    private async Task<HubConnection> MemberAsync(string name = "Ada")
    {
        var user = Guid.NewGuid();
        _boards.Grant(user, _board);
        return await ConnectAsync(user, name);
    }

    private async Task<HubConnection> ConnectAsync(Guid userId, string name = "Ada")
    {
        var server = _app.GetTestServer();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, CanvasHubMessages.HubPath), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(TestTokens.Issue(userId, name));
                options.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        _connections.Add(connection);
        _usersByConnection[connection] = userId;
        await connection.StartAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private Task JoinAsync(HubConnection connection) =>
        connection.InvokeAsync("JoinBoard", _board, TestContext.Current.CancellationToken);

    /// <summary>
    /// Completes with the first payload of a method that the server sends as (connectionId, payload) and that passes
    /// <paramref name="matches"/>. Tests wait on the event they assert, never on a delay.
    /// </summary>
    private static TaskCompletionSource<T> ListenFor<T>(HubConnection connection, string method, Func<T, bool>? matches = null)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string, T>(method, (_, payload) =>
        {
            if (matches is null || matches(payload))
            {
                completion.TrySetResult(payload);
            }
        });
        return completion;
    }
}
