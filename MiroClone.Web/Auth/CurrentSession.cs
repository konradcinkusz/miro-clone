using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MiroClone.Web.Auth;

/// <summary>The signed-in circuit's session. Pages pass its id to the typed clients; they never see a token.</summary>
public interface ICurrentSession
{
    /// <summary>The session id, or null when the circuit has no signed-in user.</summary>
    Task<string?> IdAsync();

    /// <summary>The claims of the session's current token, or null when the session has ended.</summary>
    Task<ClaimsPrincipal?> PrincipalAsync();
}

/// <summary>Reads the session from the circuit's authentication state, which the cookie fills at circuit start.</summary>
internal sealed class CircuitSession(AuthenticationStateProvider authentication, SessionService sessions) : ICurrentSession
{
    public async Task<string?> IdAsync()
    {
        var state = await authentication.GetAuthenticationStateAsync();
        return SessionClaims.SessionId(state.User);
    }

    public async Task<ClaimsPrincipal?> PrincipalAsync() =>
        await IdAsync() is { } id ? await sessions.GetPrincipalAsync(id) : null;
}
