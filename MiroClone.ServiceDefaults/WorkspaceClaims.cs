using System.Security.Claims;

namespace MiroClone.ServiceDefaults;

/// <summary>
/// Reads the two facts every request needs from an authservice token: who the user is and which workspace
/// (authservice organization) is active. A missing or malformed claim reads as null, never as a default id.
/// </summary>
public static class WorkspaceClaims
{
    /// <summary>Claim carrying the active workspace id. Aligned with authservice in ADR 0007.</summary>
    public const string WorkspaceClaimType = "workspace";

    public static Guid? UserId(ClaimsPrincipal principal) =>
        ParseGuid(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value);

    public static Guid? WorkspaceId(ClaimsPrincipal principal) =>
        ParseGuid(principal.FindFirst(WorkspaceClaimType)?.Value);

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
