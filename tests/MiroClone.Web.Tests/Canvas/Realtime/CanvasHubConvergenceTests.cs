using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiroClone.CanvasService;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Persistence;
using MiroClone.CanvasService.Realtime;
using MiroClone.ServiceDefaults;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Realtime;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Realtime;

/// <summary>
/// Two live sessions and a late joiner against the real canvas hub, hosted in-process on TestServer with the service's own wiring
/// (JWT validation through the test key, the hub, the registry). Only BoardsService's access check and the snapshot store are
/// replaced, by in-memory doubles. Nothing opens a socket, so this runs without a network.
/// </summary>
public sealed class CanvasHubConvergenceTests : IAsyncLifetime
{
    private static readonly Guid Board = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000aa");
    private static readonly Guid Workspace = Guid.Parse("cccccccc-0000-4000-8000-0000000000aa");
    private static readonly Guid Ada = Guid.Parse("11111111-1111-4111-8111-00000000000a");
    private static readonly Guid Grace = Guid.Parse("11111111-1111-4111-8111-00000000000b");
    private static readonly Guid Outsider = Guid.Parse("11111111-1111-4111-8111-00000000000c");

    private readonly RoomAccess _access = new();
    private readonly List<HubCanvasSession> _sessions = [];
    private WebApplication _server = null!;

    public async ValueTask InitializeAsync()
    {
        _access.Grant(Ada, Board);
        _access.Grant(Grace, Board);
        _server = await StartCanvasHostAsync(_access);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }

        await _server.StopAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task TwoSessionsOnTheRealHubConvergeOnEditsAndPresence()
    {
        var ada = Open(Ada, "Ada");
        var grace = Open(Grace, "Grace");
        ada.Start();
        grace.Start();
        await Eventually.AssertAsync(() => ada.Connection == CanvasConnectionState.Live && grace.Connection == CanvasConnectionState.Live, "both sessions to be live");

        var layer = Rectangle();
        ada.Commit([LayerMutation.Upsert(layer)]);
        await Eventually.AssertAsync(() => grace.Document.Contains(layer.Id), "grace to see ada's layer");

        ada.SetPresence(new Point(12, 34), [layer.Id]);
        await Eventually.AssertAsync(
            () => grace.RemoteParticipants.Any(remote => remote.Cursor == new Point(12, 34) && remote.Selection.Count == 1),
            "grace to see ada's cursor and selection");

        grace.Commit([LayerMutation.Delete(layer.Id)]);
        await Eventually.AssertAsync(() => !ada.Document.Contains(layer.Id), "ada to see grace's delete");
        Assert.Equal(ada.Document.Order, grace.Document.Order);
    }

    [Fact]
    public async Task ALateJoinerIsSentTheBoardAsItStandsAndSeesTheOthers()
    {
        var ada = Open(Ada, "Ada");
        ada.Start();
        await Eventually.AssertAsync(() => ada.Connection == CanvasConnectionState.Live, "ada to be live");
        var layer = Rectangle();
        ada.Commit([LayerMutation.Upsert(layer)]);
        await Eventually.AssertAsync(() => ada.Document.Contains(layer.Id), "the layer to reach the service");

        var grace = Open(Grace, "Grace");
        grace.Start();

        await Eventually.AssertAsync(() => grace.Document.Contains(layer.Id), "grace to be sent the board as it stands");
        await Eventually.AssertAsync(() => grace.RemoteParticipants.Count == 1, "grace to see ada in the room");
        // The service labels a member from the user id, since authservice issues no name: "Member" and the first four hex digits.
        Assert.Equal("Member 1111", Assert.Single(grace.RemoteParticipants).Name);
    }

    [Fact]
    public async Task ANonMemberIsRefusedByTheRealHubAndSeesTheBoardAsUnavailable()
    {
        var outsider = Open(Outsider, "Outsider");
        outsider.Start();

        await Eventually.AssertAsync(() => outsider.Connection == CanvasConnectionState.Unavailable, "the refusal to end the session");
        Assert.Empty(outsider.Document.Order);
    }

    private HubCanvasSession Open(Guid userId, string name)
    {
        var token = TestTokens.Issue(userId.ToString(), organizations: [Workspace.ToString()]);
        var connector = new SignalRCanvasHubConnector(
            new Uri("http://canvas.test" + CanvasHubMessages.HubPath),
            () => _server.GetTestServer().CreateHandler());
        var session = new HubCanvasSession(
            Board,
            userId,
            name,
            new CanvasDocument(),
            connector,
            _ => Task.FromResult<string?>(token),
            TimeProvider.System,
            dispatcher: null,
            logger: null,
            retryDelay: TimeSpan.FromMilliseconds(50));
        _sessions.Add(session);
        return session;
    }

    private static Layer Rectangle() => new(Guid.NewGuid(), LayerType.Rectangle, 10, 20, 100, 50, new Rgb(0xd9, 0xe2, 0xff));

    private static async Task<WebApplication> StartCanvasHostAsync(IBoardAccess access)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = TestTokens.Authority,
            ["Auth:Audience"] = TestTokens.Audience,
            ["Auth:Issuer"] = TestTokens.Issuer,
            ["Auth:RequireHttpsMetadata"] = "false",
            ["Database:Provider"] = "InMemory",
            ["Canvas:FlushInterval"] = "00:01:00",
        });
        builder.AddServiceDefaults();
        builder.Services.AddCanvasService(builder.Configuration, options => options.ConfigurationManager = TestTokens.Discovery());
        builder.Services.Replace(ServiceDescriptor.Singleton<IBoardAccess>(access));
        builder.Services.Replace(ServiceDescriptor.Singleton<ICanvasSnapshotStore>(new MemorySnapshotStore()));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<CanvasHub>(CanvasHubMessages.HubPath);
        await app.StartAsync();
        return app;
    }

    /// <summary>Grants boards to users. The subject is read from the token, as BoardsService would read it.</summary>
    private sealed class RoomAccess : IBoardAccess
    {
        private readonly HashSet<(Guid UserId, Guid BoardId)> _grants = [];

        public void Grant(Guid userId, Guid boardId) => _grants.Add((userId, boardId));

        public Task<BoardAccess> CheckAsync(Guid boardId, string bearerToken, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(bearerToken).Subject);
            return Task.FromResult(_grants.Contains((userId, boardId)) ? BoardAccess.Allowed : BoardAccess.NotFound);
        }
    }

    /// <summary>An in-memory snapshot store: the same contract as the table, without a database.</summary>
    private sealed class MemorySnapshotStore : ICanvasSnapshotStore
    {
        private readonly Dictionary<Guid, StoredSnapshot> _rows = [];

        public Task EnsureSchemaAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<StoredSnapshot>> LoadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredSnapshot>>([]);

        public Task<StoredSnapshot?> FindAsync(Guid boardId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.GetValueOrDefault(boardId));

        public Task UpsertAsync(StoredSnapshot snapshot, CancellationToken cancellationToken)
        {
            _rows[snapshot.BoardId] = snapshot;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid boardId, CancellationToken cancellationToken)
        {
            _rows.Remove(boardId);
            return Task.CompletedTask;
        }
    }
}
