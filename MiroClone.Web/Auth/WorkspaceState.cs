using System.Security.Claims;
using MiroClone.ServiceDefaults;

namespace MiroClone.Web.Auth;

public enum WorkspaceKind
{
    /// <summary>The token names exactly one organization: that is the workspace.</summary>
    Active,

    /// <summary>Several organizations, and the token names none. authservice cannot choose one yet (ADR 0011).</summary>
    ChooseOne,

    /// <summary>No organization at all.</summary>
    NoWorkspace,
}

/// <summary>
/// What the dashboard may show about the workspace. Decided from the token's claims only, so the shell and the boards service
/// agree: a user with two organizations has no active workspace here, and the boards service refuses them too.
/// </summary>
public sealed record WorkspaceState(WorkspaceKind Kind, Guid? WorkspaceId, int MembershipCount)
{
    public static WorkspaceState From(ClaimsPrincipal principal)
    {
        var memberships = principal.FindAll(WorkspaceClaims.WorkspaceClaimType)
            .Select(claim => Guid.TryParse(claim.Value, out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Distinct()
            .Count();

        var active = WorkspaceClaims.WorkspaceId(principal);
        return active is { } id
            ? new WorkspaceState(WorkspaceKind.Active, id, memberships)
            : new WorkspaceState(memberships == 0 ? WorkspaceKind.NoWorkspace : WorkspaceKind.ChooseOne, null, memberships);
    }
}
