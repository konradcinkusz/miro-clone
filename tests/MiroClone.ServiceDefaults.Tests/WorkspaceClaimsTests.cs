using System.Security.Claims;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

public sealed class WorkspaceClaimsTests
{
    private static readonly Guid UserId = Guid.Parse("5f6f1c2e-0000-4000-8000-000000000001");
    private static readonly Guid WorkspaceId = Guid.Parse("5f6f1c2e-0000-4000-8000-000000000002");
    private static readonly Guid OtherWorkspaceId = Guid.Parse("5f6f1c2e-0000-4000-8000-000000000003");

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
    public void ReadsTheWorkspaceFromTheOrganizationClaimAuthserviceIssues()
    {
        // The claim name is authservice's (TokenService.BuildClaimsAsync), pinned here as a literal on purpose (ADR 0011).
        var principal = Principal(new Claim("organization", WorkspaceId.ToString()));

        Assert.Equal("organization", WorkspaceClaims.WorkspaceClaimType);
        Assert.Equal(WorkspaceId, WorkspaceClaims.WorkspaceId(principal));
    }

    [Fact]
    public void UserInTwoOrganizationsHasNoActiveWorkspace()
    {
        var principal = Principal(
            new Claim("organization", WorkspaceId.ToString()),
            new Claim("organization", OtherWorkspaceId.ToString()));

        Assert.Null(WorkspaceClaims.WorkspaceId(principal));
    }

    [Fact]
    public void RepeatedClaimForTheSameOrganizationIsStillOneWorkspace()
    {
        var principal = Principal(
            new Claim("organization", WorkspaceId.ToString()),
            new Claim("organization", WorkspaceId.ToString()));

        Assert.Equal(WorkspaceId, WorkspaceClaims.WorkspaceId(principal));
    }

    [Fact]
    public void MissingOrMalformedClaimsReadAsNullNeverAsADefaultId()
    {
        var principal = Principal(new Claim("organization", "not-a-guid"));

        Assert.Null(WorkspaceClaims.WorkspaceId(principal));
        Assert.Null(WorkspaceClaims.WorkspaceId(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(WorkspaceClaims.UserId(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
