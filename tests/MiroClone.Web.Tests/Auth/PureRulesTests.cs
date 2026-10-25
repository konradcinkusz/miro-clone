using System.Security.Claims;
using MiroClone.Web.Auth;
using Xunit;

namespace MiroClone.Web.Tests.Auth;

public sealed class LocalReturnUrlTests
{
    [Theory]
    [InlineData("/boards/42", "/boards/42")]
    [InlineData("/", "/")]
    [InlineData("/boards?search=a", "/boards?search=a")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("boards", "/")]
    [InlineData("/ok\r\nLocation: x", "/")]
    public void OnlyPathsOnThisSiteAreTaken(string? candidate, string expected) =>
        Assert.Equal(expected, LocalReturnUrl.Resolve(candidate));

    [Fact]
    public void TheSignInAddressCarriesTheSafePath() =>
        Assert.Equal("/sign-in?returnUrl=%2Fboards%2F1", LocalReturnUrl.SignInFor("/boards/1"));

    [Fact]
    public void TheSignInAddressNeverCarriesAForeignOrigin() =>
        Assert.Equal("/sign-in?returnUrl=%2F", LocalReturnUrl.SignInFor("//evil.example"));
}

public sealed class WorkspaceStateTests
{
    private static ClaimsPrincipal Principal(params string[] organizations) =>
        new(new ClaimsIdentity(organizations.Select(id => new Claim("organization", id)), "test"));

    [Fact]
    public void ANoOrganizationUserHasNoWorkspace()
    {
        var state = WorkspaceState.From(Principal());

        Assert.Equal(WorkspaceKind.NoWorkspace, state.Kind);
        Assert.Null(state.WorkspaceId);
    }

    [Fact]
    public void OneOrganizationIsTheActiveWorkspace()
    {
        var id = Guid.NewGuid();

        var state = WorkspaceState.From(Principal(id.ToString()));

        Assert.Equal(WorkspaceKind.Active, state.Kind);
        Assert.Equal(id, state.WorkspaceId);
        Assert.Equal(1, state.MembershipCount);
    }

    [Fact]
    public void TwoOrganizationsLeaveNoActiveWorkspaceAndFailClosed()
    {
        var state = WorkspaceState.From(Principal(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

        Assert.Equal(WorkspaceKind.ChooseOne, state.Kind);
        Assert.Null(state.WorkspaceId);
        Assert.Equal(2, state.MembershipCount);
    }

    [Fact]
    public void AMalformedOrganizationClaimFailsClosedLikeTheServices()
    {
        // WorkspaceClaims.WorkspaceId counts every organization claim, so one unreadable claim leaves no active workspace,
        // exactly as the boards service decides it. The valid membership is still counted.
        var id = Guid.NewGuid();

        var state = WorkspaceState.From(Principal(id.ToString(), "not-a-guid"));

        Assert.Equal(WorkspaceKind.ChooseOne, state.Kind);
        Assert.Null(state.WorkspaceId);
        Assert.Equal(1, state.MembershipCount);
    }
}
