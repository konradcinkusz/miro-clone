using System.Security.Claims;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

public sealed class WorkspaceClaimsTests
{
    private static readonly Guid UserId = Guid.Parse("5f6f1c2e-0000-4000-8000-000000000001");
    private static readonly Guid WorkspaceId = Guid.Parse("5f6f1c2e-0000-4000-8000-000000000002");

    [Fact]
    public void ReadsUserIdFromSubWhenTheClaimWasNotRemapped()
    {
        var principal = Principal(new Claim("sub", UserId.ToString()));

        Assert.Equal(UserId, WorkspaceClaims.UserId(principal));
    }

    [Fact]
    public void PrefersNameIdentifierWhenPresent()
    {
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString()));

        Assert.Equal(UserId, WorkspaceClaims.UserId(principal));
    }

    [Fact]
    public void ReadsTheActiveWorkspace()
    {
        var principal = Principal(new Claim(WorkspaceClaims.WorkspaceClaimType, WorkspaceId.ToString()));

        Assert.Equal(WorkspaceId, WorkspaceClaims.WorkspaceId(principal));
    }

    [Fact]
    public void MissingOrMalformedClaimsReadAsNullNeverAsADefaultId()
    {
        var principal = Principal(new Claim(WorkspaceClaims.WorkspaceClaimType, "not-a-guid"));

        Assert.Null(WorkspaceClaims.WorkspaceId(principal));
        Assert.Null(WorkspaceClaims.UserId(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
