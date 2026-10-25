using System.Security.Claims;

namespace MiroClone.ServiceDefaults;

/// <summary>
/// Reads the two facts every request needs from an authservice token: who the user is and which workspace
/// (authservice organization) is active. A missing or malformed claim reads as null, never as a default id.
/// </summary>
public static class WorkspaceClaims
{
    /// <summary>
    /// Claim carrying an organization id. authservice writes one claim per membership (ADR 0011), which is the
    /// name this repository uses for a workspace.
    /// </summary>
    public const string WorkspaceClaimType = "organization";

    public static Guid? UserId(ClaimsPrincipal principal) =>
        ParseGuid(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value);

    /// <summary>
    /// The active workspace. authservice names no active one, so a token must carry exactly one organization to yield an id.
    /// A user in two organizations reads as null, and the caller refuses rather than guess (ADR 0011).
    /// </summary>
    public static Guid? WorkspaceId(ClaimsPrincipal principal)
    {
        var ids = principal.FindAll(WorkspaceClaimType).Select(claim => ParseGuid(claim.Value)).Distinct().ToArray();
        return ids is [{ } only] ? only : null;
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
