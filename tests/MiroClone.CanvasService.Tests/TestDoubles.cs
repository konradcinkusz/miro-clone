using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using MiroClone.TestSupport;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Persistence;

namespace MiroClone.CanvasService.Tests;

/// <summary>
/// Tokens for the canvas tests, signed by the shared test authority (WP03), so the real JWKS path runs with no network. A user
/// belongs to one workspace unless a test issues several organisations, which is how authservice mints a membership.
/// </summary>
internal static class TestTokens
{
    /// <summary>The one audience every estate token carries (docs/decisions/0011-authservice-adoption-and-token-contract.md).</summary>
    public const string Audience = "miro-clone";

    /// <summary>The workspace most tests act in.</summary>
    public static readonly Guid Workspace = Guid.NewGuid();

    /// <summary>One authority for the test run, started on first use and kept for the run.</summary>
    public static readonly Task<TestAuthority> Authority = TestAuthority.StartAsync();

    public static string Issue(Guid userId, Guid? workspace = null, string audience = Audience) =>
        Authority.Result.IssueToken(audience, userId, [workspace ?? Workspace], DateTime.UtcNow.AddMinutes(10));

    public static string IssueForOrganizations(Guid userId, params Guid[] organizationIds) =>
        Authority.Result.IssueToken(Audience, userId, organizationIds, DateTime.UtcNow.AddMinutes(10));
}

/// <summary>
/// Stands in for BoardsService: a board is visible to a user when granted to that user, read from the token's subject.
/// The access check can be made to fail, to exercise the fail-closed paths.
/// </summary>
internal sealed class FakeBoardAccess : IBoardAccess
{
    private readonly HashSet<(Guid UserId, Guid BoardId)> _grants = [];

    public bool Unavailable { get; set; }

    public void Grant(Guid userId, Guid boardId) => _grants.Add((userId, boardId));

    public Task<BoardAccess> CheckAsync(Guid boardId, string bearerToken, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new BoardsServiceException(StatusCodes.Status503ServiceUnavailable, "BoardsService is down for the test.");
        }

        var userId = Guid.Parse(new JsonWebToken(bearerToken).Subject);
        return Task.FromResult(_grants.Contains((userId, boardId)) ? BoardAccess.Allowed : BoardAccess.NotFound);
    }
}

/// <summary>An in-memory snapshot store that can be taken offline, to exercise the degraded paths.</summary>
internal sealed class FakeSnapshotStore : ICanvasSnapshotStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, StoredSnapshot> _rows = [];

    public bool Offline { get; set; }

    public int RowCount
    {
        get
        {
            lock (_lock)
            {
                return _rows.Count;
            }
        }
    }

    public StoredSnapshot? Row(Guid boardId)
    {
        lock (_lock)
        {
            return _rows.GetValueOrDefault(boardId);
        }
    }

    public Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        RequireOnline();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredSnapshot>> LoadRecentAsync(int limit, CancellationToken cancellationToken)
    {
        RequireOnline();
        lock (_lock)
        {
            IReadOnlyList<StoredSnapshot> recent = _rows.Values.OrderByDescending(row => row.UpdatedAt).Take(limit).ToList();
            return Task.FromResult(recent);
        }
    }

    public Task<StoredSnapshot?> FindAsync(Guid boardId, CancellationToken cancellationToken)
    {
        RequireOnline();
        return Task.FromResult(Row(boardId));
    }

    public Task UpsertAsync(StoredSnapshot snapshot, CancellationToken cancellationToken)
    {
        RequireOnline();
        lock (_lock)
        {
            _rows[snapshot.BoardId] = snapshot;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid boardId, CancellationToken cancellationToken)
    {
        RequireOnline();
        lock (_lock)
        {
            _rows.Remove(boardId);
        }

        return Task.CompletedTask;
    }

    private void RequireOnline()
    {
        if (Offline)
        {
            throw new InvalidOperationException("The snapshot store is offline for the test.");
        }
    }
}
