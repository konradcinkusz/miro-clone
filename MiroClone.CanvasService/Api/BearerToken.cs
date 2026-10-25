using Microsoft.AspNetCore.Http;

namespace MiroClone.CanvasService.Api;

/// <summary>
/// Reads the caller's bearer token so it can be forwarded to BoardsService. Browsers cannot set headers on a websocket,
/// so the hub's token also arrives as <c>access_token</c> in the query string (the SignalR convention).
/// </summary>
public static class BearerToken
{
    private const string Prefix = "Bearer ";
    private const string QueryName = "access_token";

    public static string? From(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && header.Length > Prefix.Length)
        {
            return header[Prefix.Length..];
        }

        var query = request.Query[QueryName].ToString();
        return query.Length > 0 ? query : null;
    }
}
