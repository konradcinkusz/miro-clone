using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MiroClone.CanvasService.Api;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.CanvasService.Realtime;

/// <summary>
/// The realtime surface for one board at a time: join, mutate, announce presence. Every join is checked against
/// BoardsService (a non-member gets "board_not_found", the same answer as a missing board). A joined connection can
/// only send to the board it joined, and each connection is rate limited (service-api-patterns §1).
/// </summary>
[Authorize]
public sealed partial class CanvasHub(
    CanvasRegistry registry,
    IBoardAccess boardAccess,
    PresenceTracker presence,
    ILogger<CanvasHub> logger) : Hub
{
    private const string ConnectionKey = "canvas.connection";

    public override Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext() is { } http ? BearerToken.From(http.Request) : null;
        if (token is null)
        {
            Context.Abort();
            return Task.CompletedTask;
        }

        Context.Items[ConnectionKey] = new ConnectionState(token);
        return base.OnConnectedAsync();
    }

    /// <summary>
    /// Joins the board's group, then sends the snapshot and the other participants. The board gate is held across both,
    /// so a change is either inside the snapshot or broadcast after it, never lost between them.
    /// </summary>
    public async Task JoinBoard(Guid boardId)
    {
        var connection = State();
        var canvas = await LoadJoinableAsync(boardId, connection);

        if (connection.Board is { } previous && previous != boardId)
        {
            await LeaveAsync(previous, Context.ConnectionAborted);
        }

        await canvas.Gate.WaitAsync(Context.ConnectionAborted);
        try
        {
            connection.Board = boardId;
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(boardId), Context.ConnectionAborted);

            var self = Stamp(Context.User, cursor: null, selection: []);
            presence.Upsert(boardId, Context.ConnectionId, self);
            await Clients.Caller.SendAsync(CanvasHubMessages.InitialState, canvas.Snapshot(), presence.Snapshot(boardId), Context.ConnectionAborted);
            await Clients.OthersInGroup(GroupName(boardId)).SendAsync(CanvasHubMessages.PresenceUpdated, Context.ConnectionId, self, Context.ConnectionAborted);
        }
        finally
        {
            canvas.Gate.Release();
        }
    }

    /// <summary>Applies a mutation and broadcasts it to the other participants, in apply order. Replays are not broadcast.</summary>
    public async Task SendMutation(Guid boardId, LayerMutation mutation)
    {
        var connection = JoinedConnection(boardId);
        if (!connection.TryTakeMutation())
        {
            throw new HubException(CanvasHubMessages.RateLimited);
        }

        var canvas = registry.TryGet(boardId) ?? throw new HubException(CanvasHubMessages.NotJoined);
        MutationStatus status;
        await canvas.Gate.WaitAsync(Context.ConnectionAborted);
        try
        {
            status = canvas.Apply(mutation);
            if (status == MutationStatus.Changed)
            {
                registry.MarkDirty(boardId);
                await Clients.OthersInGroup(GroupName(boardId))
                    .SendAsync(CanvasHubMessages.LayerMutation, Context.ConnectionId, mutation, Context.ConnectionAborted);
            }
        }
        finally
        {
            canvas.Gate.Release();
        }

        switch (status)
        {
            case MutationStatus.LayerLimitReached:
                throw new HubException(CanvasHubMessages.LayerLimit);
            case MutationStatus.Invalid:
                throw new HubException(CanvasHubMessages.InvalidLayer);
        }
    }

    /// <summary>Updates this connection's cursor and selection. Identity is stamped from the token, whatever the client sent.</summary>
    public async Task UpdatePresence(Guid boardId, Presence update)
    {
        var connection = JoinedConnection(boardId);
        if (!connection.TryTakePresence())
        {
            throw new HubException(CanvasHubMessages.RateLimited);
        }

        if (update.Cursor is { } cursor && !(double.IsFinite(cursor.X) && double.IsFinite(cursor.Y)))
        {
            throw new HubException(CanvasHubMessages.InvalidPresence);
        }

        if (!LayerRules.IsValidSelection(update.Selection))
        {
            throw new HubException(CanvasHubMessages.InvalidPresence);
        }

        var stamped = Stamp(Context.User, update.Cursor, update.Selection);
        presence.Upsert(boardId, Context.ConnectionId, stamped);
        await Clients.OthersInGroup(GroupName(boardId))
            .SendAsync(CanvasHubMessages.PresenceUpdated, Context.ConnectionId, stamped, Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.Remove(ConnectionKey, out var value) && value is ConnectionState connection)
        {
            if (connection.Board is { } board)
            {
                // The connection is already aborted here, so the leave must not be cancelled with it.
                await LeaveAsync(board, CancellationToken.None);
            }

            connection.Dispose();
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<BoardCanvas> LoadJoinableAsync(Guid boardId, ConnectionState connection)
    {
        try
        {
            var access = await boardAccess.CheckAsync(boardId, connection.BearerToken, Context.ConnectionAborted);
            if (access == BoardAccess.NotFound)
            {
                throw new HubException(CanvasHubMessages.NotFound);
            }

            return await registry.GetOrLoadAsync(boardId, Context.ConnectionAborted);
        }
        catch (BoardsServiceException ex)
        {
            LogJoinDegraded(logger, boardId, ex.Message);
            throw new HubException(CanvasHubMessages.Unavailable);
        }
        catch (CanvasStoreUnavailableException ex)
        {
            LogJoinDegraded(logger, boardId, ex.Message);
            throw new HubException(CanvasHubMessages.Unavailable);
        }
    }

    private async Task LeaveAsync(Guid boardId, CancellationToken cancellationToken)
    {
        presence.Remove(boardId, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(boardId), cancellationToken);
        await Clients.OthersInGroup(GroupName(boardId))
            .SendAsync(CanvasHubMessages.PresenceLeft, Context.ConnectionId, cancellationToken);
    }

    private ConnectionState State() =>
        Context.Items.TryGetValue(ConnectionKey, out var value) && value is ConnectionState connection
            ? connection
            : throw new HubException(CanvasHubMessages.NotJoined);

    private ConnectionState JoinedConnection(Guid boardId)
    {
        var connection = State();
        return connection.Board == boardId ? connection : throw new HubException(CanvasHubMessages.NotJoined);
    }

    private static Presence Stamp(ClaimsPrincipal? user, Point? cursor, IReadOnlyList<Guid> selection)
    {
        var userId = user is null ? null : WorkspaceClaims.UserId(user);
        if (user is null || userId is not { } id)
        {
            throw new HubException(CanvasHubMessages.Unauthenticated);
        }

        var name = user.FindFirst("name")?.Value?.Trim();
        return new Presence(
            id,
            string.IsNullOrWhiteSpace(name) ? "Member" : name[..Math.Min(name.Length, MaxNameLength)],
            PresenceColors.For(id),
            cursor,
            selection);
    }

    private static string GroupName(Guid boardId) => $"board:{boardId:D}";

    private const int MaxNameLength = 64;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Joining board {BoardId} is unavailable: {Error}")]
    private static partial void LogJoinDegraded(ILogger logger, Guid boardId, string error);

    /// <summary>Per-connection state: the forwarded token, the board joined, and the two rate limits.</summary>
    private sealed class ConnectionState(string bearerToken) : IDisposable
    {
        private readonly TokenBucketRateLimiter _mutations = Bucket(tokenLimit: 120, tokensPerSecond: 60);
        private readonly TokenBucketRateLimiter _presence = Bucket(tokenLimit: 60, tokensPerSecond: 30);

        public string BearerToken { get; } = bearerToken;

        public Guid? Board { get; set; }

        public bool TryTakeMutation() => TryTake(_mutations);

        public bool TryTakePresence() => TryTake(_presence);

        public void Dispose()
        {
            _mutations.Dispose();
            _presence.Dispose();
        }

        private static TokenBucketRateLimiter Bucket(int tokenLimit, int tokensPerSecond) => new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = tokenLimit,
            TokensPerPeriod = tokensPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueLimit = 0,
        });

        private static bool TryTake(TokenBucketRateLimiter limiter)
        {
            using var lease = limiter.AttemptAcquire(1);
            return lease.IsAcquired;
        }
    }
}

/// <summary>Presence colours are chosen from the user id, so one person keeps one colour across sessions.</summary>
internal static class PresenceColors
{
    private static readonly string[] Palette = ["#e11d48", "#2563eb", "#059669", "#d97706", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    public static string For(Guid userId) => Palette[userId.ToByteArray()[0] % Palette.Length];
}
