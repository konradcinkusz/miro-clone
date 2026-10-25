using System.Net;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.Web.Api;
using MiroClone.Web.Auth;
using MiroClone.Web.Components.Pages;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>The dashboard shell: each workspace state it can be in, and the alert when a load fails (blazor-web-app §5).</summary>
public sealed class HomeShellTests : IDisposable
{
    private static readonly Guid Studio = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    private readonly BunitContext _context = new();
    private readonly ScriptedHandler _authority = new();
    private readonly ScriptedHandler _boards = new();

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task AnActiveWorkspaceWithNoBoardsShowsTheEmptyState()
    {
        Arrange(organizations: [("studio", Studio, "Owner")]);
        _boards.On(HttpMethod.Get, "api/boards", HttpStatusCode.OK, new { items = Array.Empty<object>(), total = 0 });

        var page = Render(Session(Principal(Studio)));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='boards-empty']")));
        Assert.Contains("Workspace: studio", page.Find("[data-testid='workspace-name']").TextContent);
    }

    [Fact]
    public async Task TwoOrganizationsShowTheChoiceAndNoBoardsAreAskedFor()
    {
        var principal = Principal(Studio, Other);
        Arrange(organizations: [("studio", Studio, "Owner"), ("archive", Other, "Member")]);

        var page = Render(Session(principal));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='workspace-choose']")));
        var choice = page.Find("[data-testid='workspace-choose']").TextContent;
        Assert.Contains("You belong to 2 workspaces", choice);
        Assert.Contains("archive", choice);
        Assert.Empty(_boards.Calls);
    }

    [Fact]
    public async Task NoOrganizationShowsThatNoWorkspaceExists()
    {
        var principal = Principal();
        Arrange(organizations: []);

        var page = Render(Session(principal));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='workspace-none']")));
        Assert.Empty(_boards.Calls);
    }

    [Fact]
    public async Task AFailedBoardsLoadShowsOneAlertAndTheShellStillRenders()
    {
        Arrange(organizations: [("studio", Studio, "Owner")]);
        _boards.On(HttpMethod.Get, "api/boards", HttpStatusCode.InternalServerError, new { error = "boom" });

        var page = Render(Session(Principal(Studio)));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[role='alert']")));
        Assert.Contains("could not be loaded", page.Find("[role='alert']").TextContent);
        Assert.NotNull(page.Find("h1"));
    }

    [Fact]
    public async Task AnEndedSessionSendsTheVisitorToSignIn()
    {
        Arrange(organizations: []);

        var page = Render(Session(null));

        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        page.WaitForAssertion(() => Assert.StartsWith("http://localhost/sign-in", navigation.Uri, StringComparison.Ordinal));
        Assert.Contains("returnUrl=%2F", navigation.Uri, StringComparison.Ordinal);
    }

    private void Arrange(IReadOnlyList<(string Name, Guid Id, string Role)> organizations)
    {
        _context.AddAuthorization().SetAuthorized("owner@dev.test");
        _authority.On(HttpMethod.Get, "api/v1/organizations", HttpStatusCode.OK, organizations
            .Select(org => new { id = org.Id.ToString("D"), name = org.Name, userRole = org.Role, imageUrl = (string?)null })
            .ToArray());

        var authority = new AuthorityClient(new HttpClient(_authority) { BaseAddress = new Uri("http://authority.test/") });
        var boards = new BoardsClient(new HttpClient(_boards) { BaseAddress = new Uri("http://boards-service/") });
        _context.Services.AddSingleton(authority);
        _context.Services.AddSingleton(boards);
    }

    private IRenderedComponent<Home> Render(ICurrentSession session)
    {
        _context.Services.AddSingleton(session);
        return _context.Render<Home>();
    }

    private static FakeSession Session(ClaimsPrincipal? principal) => new(principal);

    private static ClaimsPrincipal Principal(params Guid[] organizations) =>
        new(new ClaimsIdentity(
            [new Claim("sub", "11111111-1111-1111-1111-111111111111"), .. organizations.Select(id => new Claim("organization", id.ToString("D")))],
            SessionClaims.AuthenticationType));

    private sealed class FakeSession(ClaimsPrincipal? principal) : ICurrentSession
    {
        public Task<string?> IdAsync() => Task.FromResult(principal is null ? null : "session-1");

        public Task<ClaimsPrincipal?> PrincipalAsync() => Task.FromResult(principal);
    }
}
