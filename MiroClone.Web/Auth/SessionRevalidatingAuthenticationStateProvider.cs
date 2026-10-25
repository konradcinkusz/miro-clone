using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiroClone.Web.Auth;

/// <summary>
/// Ends a circuit whose session has ended. A cookie can outlive the server-side session (sign-out elsewhere, a refresh the
/// service refused, the absolute lifetime), so every circuit asks the store again on an interval (blazor-web-app §7).
/// </summary>
internal sealed class SessionRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    SessionService sessions,
    IOptions<SessionSettings> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => options.Value.RevalidationInterval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var sessionId = SessionClaims.SessionId(authenticationState.User);
        return sessionId is not null && await sessions.ExistsAsync(sessionId, cancellationToken);
    }
}
