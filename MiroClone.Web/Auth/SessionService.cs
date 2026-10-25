using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MiroClone.Web.Auth;

/// <summary>Configuration under the "Session" section.</summary>
public sealed class SessionSettings
{
    public const string SectionName = "Session";

    /// <summary>Absolute lifetime of a sign-in. The cookie and the stored session end together.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>A token this close to expiry is refreshed before it is handed to a service.</summary>
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often a live circuit asks whether its session still exists (blazor-web-app §7).</summary>
    public TimeSpan RevalidationInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// The server-side session: the access and refresh tokens of one sign-in, held in the distributed cache (blazor-web-app §7).
/// The browser holds only a cookie that names the session id, so no token reaches the browser (ADR 0022).
/// Refresh is single-flight per session: authservice rotates refresh tokens, and a second use of a spent one revokes the family.
/// </summary>
public sealed class SessionService(
    IDistributedCache cache,
    IServiceScopeFactory scopes,
    AccessTokenValidator validator,
    IOptions<SessionSettings> options,
    TimeProvider clock)
{
    private const string KeyPrefix = "session:";
    private const int StripeCount = 32;

    // Striped locks: one refresh at a time per stripe, with no per-session objects left behind after a sign-out.
    private static readonly SemaphoreSlim[] Stripes = Enumerable.Range(0, StripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private SessionSettings Settings => options.Value;

    /// <summary>Stores a new sign-in and returns the session id the cookie will carry.</summary>
    public async Task<string> CreateAsync(AuthorityTokens tokens, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();
        var record = new SessionRecord(
            tokens.AccessToken,
            tokens.RefreshToken,
            now.AddSeconds(tokens.ExpiresIn),
            now.Add(Settings.Lifetime),
            principal.Claims.Select(claim => new ClaimPair(claim.Type, claim.Value)).ToList());
        await SaveAsync(id, record, ct);
        return id;
    }

    /// <summary>Whether the session exists and has not reached its absolute end.</summary>
    public async Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default)
    {
        var record = await LoadAsync(sessionId, ct);
        return record is not null && record.ExpiresAt > clock.GetUtcNow();
    }

    /// <summary>The claims of the session's current token, as of its last refresh. Null when the session is gone.</summary>
    public async Task<ClaimsPrincipal?> GetPrincipalAsync(string sessionId, CancellationToken ct = default)
    {
        var record = await LoadAsync(sessionId, ct);
        if (record is null)
        {
            return null;
        }

        var identity = new ClaimsIdentity(record.Claims.Select(claim => new Claim(claim.Type, claim.Value)), SessionClaims.AuthenticationType);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// The access token to send for this session, refreshed first when it is close to expiry. Null when the session has
    /// ended, which the caller must treat as a sign-out.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(string sessionId, CancellationToken ct = default)
    {
        var record = await LoadAsync(sessionId, ct);
        if (record is null || record.ExpiresAt <= clock.GetUtcNow())
        {
            await RemoveAsync(sessionId, ct);
            return null;
        }

        if (!NeedsRefresh(record))
        {
            return record.AccessToken;
        }

        var stripe = Stripes[(uint)sessionId.GetHashCode() % StripeCount];
        await stripe.WaitAsync(ct);
        try
        {
            // Another request may have refreshed while this one waited for the stripe.
            record = await LoadAsync(sessionId, ct);
            if (record is null)
            {
                return null;
            }

            return NeedsRefresh(record) ? await RefreshAsync(sessionId, record, ct) : record.AccessToken;
        }
        finally
        {
            stripe.Release();
        }
    }

    /// <summary>Ends the session: the cookie's partner in the store is removed. Idempotent.</summary>
    public Task RemoveAsync(string sessionId, CancellationToken ct = default) => cache.RemoveAsync(KeyOf(sessionId), ct);

    private async Task<string?> RefreshAsync(string sessionId, SessionRecord record, CancellationToken ct)
    {
        // The authservice client is resolved from a scope of its own: this runs under a stripe, outside any circuit's scope.
        using var scope = scopes.CreateScope();
        var authority = scope.ServiceProvider.GetRequiredService<AuthorityClient>();
        var refreshed = await authority.RefreshAsync(record.RefreshToken, ct);

        if (!refreshed.Succeeded)
        {
            // 400 and 401 mean authservice no longer accepts this session. Anything else is an outage: the caller sees the failure.
            if (refreshed.Status is 400 or 401)
            {
                await RemoveAsync(sessionId, ct);
                return null;
            }

            throw new InvalidOperationException($"authservice refresh failed with status {refreshed.Status}.");
        }

        ClaimsPrincipal principal;
        try
        {
            principal = await validator.ValidateAsync(refreshed.Value!.AccessToken, ct);
        }
        catch (SecurityTokenException)
        {
            await RemoveAsync(sessionId, ct);
            return null;
        }

        var now = clock.GetUtcNow();
        var updated = record with
        {
            AccessToken = refreshed.Value.AccessToken,
            RefreshToken = refreshed.Value.RefreshToken,
            AccessExpiresAt = now.AddSeconds(refreshed.Value.ExpiresIn),
            Claims = principal.Claims.Select(claim => new ClaimPair(claim.Type, claim.Value)).ToList(),
        };
        await SaveAsync(sessionId, updated, ct);
        return updated.AccessToken;
    }

    private bool NeedsRefresh(SessionRecord record) => record.AccessExpiresAt - Settings.RefreshBefore <= clock.GetUtcNow();

    private async Task SaveAsync(string sessionId, SessionRecord record, CancellationToken ct)
    {
        var entry = new DistributedCacheEntryOptions { AbsoluteExpiration = record.ExpiresAt };
        await cache.SetStringAsync(KeyOf(sessionId), JsonSerializer.Serialize(record, Json), entry, ct);
    }

    private async Task<SessionRecord?> LoadAsync(string sessionId, CancellationToken ct)
    {
        var json = await cache.GetStringAsync(KeyOf(sessionId), ct);
        return json is null ? null : JsonSerializer.Deserialize<SessionRecord>(json, Json);
    }

    private static string KeyOf(string sessionId) => KeyPrefix + sessionId;

    /// <summary>What the store keeps for one sign-in. Tokens never leave the server.</summary>
    internal sealed record SessionRecord(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessExpiresAt,
        DateTimeOffset ExpiresAt,
        List<ClaimPair> Claims);

    internal sealed record ClaimPair(string Type, string Value);
}
