using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Persistence;

namespace MiroClone.CanvasService.Tests;

/// <summary>
/// Issues RS256 tokens from one generated key, and serves that key through a static configuration manager, so the
/// JWT path runs with no authority and no network (ADR 0007).
/// </summary>
internal static class TestTokens
{
    public const string Authority = "https://auth.test";
    public const string Audience = "miro-clone-canvas";
    private const string KeyId = "canvas-test-key";

    private static readonly RSA SigningKey = RSA.Create(2048);

    public static void ConfigureBearer(JwtBearerOptions options)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Authority };
        configuration.SigningKeys.Add(new RsaSecurityKey(SigningKey.ExportParameters(includePrivateParameters: false))
        {
            KeyId = KeyId,
        });
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }

    public static string Issue(Guid userId, string name = "Ada", string audience = Audience)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString("D")), new Claim("name", name)]),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(SigningKey) { KeyId = KeyId },
                SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
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
