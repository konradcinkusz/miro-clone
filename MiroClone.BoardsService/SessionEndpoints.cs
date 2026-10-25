using System.Security.Claims;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService;

public static class SessionEndpoints
{
    /// <summary>
    /// Reports the caller's identity as the bearer token states it. A WP03 stub that proves the token path end to end:
    /// a valid token answers 200, no token answers 401. It reads no board data, and WP04 scopes every board endpoint to the workspace.
    /// </summary>
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/session", (ClaimsPrincipal user) => Results.Ok(new
        {
            userId = WorkspaceClaims.UserId(user),
            workspaceId = WorkspaceClaims.WorkspaceId(user),
        })).RequireAuthorization();

        return endpoints;
    }
}
