using System.Net;
using System.Net.Http.Headers;

namespace MiroClone.Web.Auth;

/// <summary>
/// The request option that names the session a call is made for. A typed client sets it from the circuit's own session
/// (the circuit is the only place that knows it); the handler reads it. Handlers run in their own scope, so the session
/// is passed on the request, never injected into the handler (blazor-web-app §7).
/// </summary>
public static class SessionRequest
{
    public static readonly HttpRequestOptionsKey<string> SessionId = new("MiroClone.SessionId");
}

/// <summary>
/// Adds the session's access token to a service call and refreshes it first when it is close to expiry. A call for a
/// session that has ended gets a 401 without leaving the process.
/// </summary>
public sealed class BearerTokenHandler(SessionService sessions) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(SessionRequest.SessionId, out var sessionId))
        {
            var token = await sessions.GetAccessTokenAsync(sessionId, cancellationToken);
            if (token is null)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
