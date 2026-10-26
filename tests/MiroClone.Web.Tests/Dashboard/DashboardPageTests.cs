using System.Net;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.Web.Api;
using MiroClone.Web.Auth;
using MiroClone.Web.Components.Dashboard;
using MiroClone.Web.Components.Pages;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests.Dashboard;

/// <summary>
/// The dashboard as a user meets it: the lists, the empty states, every board action, the pager, the search and the invitation.
/// The boards service is an in-memory fake behind the typed client, so each action changes what the next list returns.
/// </summary>
public sealed class DashboardPageTests : IDisposable
{
    private static readonly Guid Studio = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Colleague = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly BunitContext _context = new();
    private readonly ScriptedHandler _authority = new();
    private readonly FakeBoardsService _boards = new() { User = Me, Now = Now };

    public void Dispose() => _context.Dispose();

    [Fact]
    public void CardsShowTheBoardsWithTheirFavouriteStateAndWhoWroteThem()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada", favourite: true, minutesAgo: 0), Board("Sketch", Colleague, "Grace", minutesAgo: 60));

        var page = Render();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid='board-card']").Count));
        var first = page.FindAll("[data-testid='board-card']")[0];
        Assert.Contains("Roadmap", first.TextContent);
        Assert.Contains("You, just now", first.TextContent);
        Assert.Equal("true", first.QuerySelector("[data-testid='favourite-toggle']")!.GetAttribute("aria-pressed"));

        var second = page.FindAll("[data-testid='board-card']")[1];
        Assert.Contains("Grace, 1 hour ago", second.TextContent);
        Assert.Equal("false", second.QuerySelector("[data-testid='favourite-toggle']")!.GetAttribute("aria-pressed"));
    }

    [Fact]
    public void AWorkspaceWithNoBoardsOffersTheFirstOne()
    {
        Arrange();

        var page = Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='boards-empty']")));
        Assert.NotNull(page.Find("[data-testid='empty-create']"));
    }

    [Fact]
    public void ASearchWithNoMatchSaysSo()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));

        var page = Render(search: "zzz");

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='search-empty']")));
        Assert.Contains("No results found.", page.Find("[data-testid='search-empty']").TextContent);
    }

    [Fact]
    public void TheFavouritesViewWithNoFavouritesSaysSo()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));

        var page = Render(favourites: true);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='favourites-empty']")));
        Assert.Contains("Favourite boards", page.Find("[data-testid='dashboard-heading']").TextContent);
    }

    [Fact]
    public void CreatingABoardSendsTheTitleAndOpensTheNewBoard()
    {
        Arrange();
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='empty-create']")));

        page.Find("[data-testid='empty-create']").Click();
        page.Find("[data-testid='board-title-input']").Input("  Roadmap  ");
        page.Find("[data-testid='board-title-form']").Submit();

        var created = Assert.Single(_boards.Boards, board => board.Title == "Roadmap");
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        page.WaitForAssertion(() => Assert.EndsWith($"/board/{created.Id:D}", navigation.Uri));
        Assert.Contains(_boards.Calls, call => call.Method == "POST" && call.Path == "api/boards" && call.Body!.Contains("\"title\":\"Roadmap\""));
    }

    [Fact]
    public void ABlankTitleIsNotSentAndSaysWhatIsWrong()
    {
        Arrange();
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='empty-create']")));

        page.Find("[data-testid='empty-create']").Click();
        page.Find("[data-testid='board-title-form']").Submit();

        Assert.Equal("Enter a title of 1 to 60 characters.", page.Find("[data-testid='board-title-error']").TextContent);
        Assert.DoesNotContain(_boards.Calls, call => call.Method == "POST");
    }

    [Fact]
    public void RenamingChangesTheCardOnceTheServiceAnswers()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='board-card']")));

        page.Find("[data-testid='board-actions']").Click();
        page.Find("[data-testid='board-rename']").Click();
        page.Find("[data-testid='board-title-input']").Input("Q4 plan");
        page.Find("[data-testid='board-title-form']").Submit();

        page.WaitForAssertion(() => Assert.Contains("Q4 plan", page.Find("[data-testid='board-grid']").TextContent));
        Assert.Equal("Q4 plan", _boards.Boards.Single().Title);
        Assert.Contains(_boards.Calls, call => call.Method == "PATCH");
    }

    [Fact]
    public void ARenameTheServiceRefusesLeavesTheCardAsItWas()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='board-card']")));
        page.Find("[data-testid='board-actions']").Click();
        page.Find("[data-testid='board-rename']").Click();

        _boards.FailNext = HttpStatusCode.NotFound;
        page.Find("[data-testid='board-title-input']").Input("Q4 plan");
        page.Find("[data-testid='board-title-form']").Submit();

        page.WaitForAssertion(() => Assert.Contains("no longer exists", page.Find("[data-testid='rename-dialog'] [role='alert']").TextContent));
        Assert.Equal("Roadmap", _boards.Boards.Single().Title);
    }

    [Fact]
    public void DeletingAsksFirstThenRemovesTheBoard()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='board-card']")));

        page.Find("[data-testid='board-actions']").Click();
        page.Find("[data-testid='board-delete']").Click();

        Assert.Contains("Roadmap", page.Find("[data-testid='confirm-delete']").TextContent);
        Assert.DoesNotContain(_boards.Calls, call => call.Method == "DELETE");

        page.Find("[data-testid='confirm-delete-submit']").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='boards-empty']")));
        Assert.Empty(_boards.Boards);
        Assert.Single(_boards.Calls, call => call.Method == "DELETE");
    }

    [Fact]
    public void AFavouriteIsShownOnlyAfterTheServiceAnswers()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='favourite-toggle']")));

        page.Find("[data-testid='favourite-toggle']").Click();

        page.WaitForAssertion(() => Assert.Equal("true", page.Find("[data-testid='favourite-toggle']").GetAttribute("aria-pressed")));
        Assert.True(_boards.Boards.Single().IsFavourite);
        Assert.Contains(_boards.Calls, call => call.Method == "PUT" && call.Path.EndsWith("/favourite", StringComparison.Ordinal));
    }

    [Fact]
    public void AFavouriteTheServiceRefusesIsNotShownAsSet()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='favourite-toggle']")));

        _boards.FailNext = HttpStatusCode.InternalServerError;
        page.Find("[data-testid='favourite-toggle']").Click();

        page.WaitForAssertion(() => Assert.Contains("Could not update the favourite", page.Find("[role='alert']").TextContent));
        Assert.Equal("false", page.Find("[data-testid='favourite-toggle']").GetAttribute("aria-pressed"));
        Assert.False(_boards.Boards.Single().IsFavourite);
    }

    [Fact]
    public void UnfavouritingOnTheFavouritesViewTakesTheBoardOut()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada", favourite: true));
        var page = Render(favourites: true);
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='favourite-toggle']")));

        page.Find("[data-testid='favourite-toggle']").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='favourites-empty']")));
        Assert.Contains(_boards.Calls, call => call.Method == "DELETE" && call.Path.EndsWith("/favourite", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePagerFollowsTheAddressAndLinksOnlyToRealPages()
    {
        Arrange();
        for (var i = 0; i < 45; i++)
        {
            _boards.Add(Board($"Board {i:D2}", Me, "Ada", minutesAgo: i));
        }

        var first = Render();
        first.WaitForAssertion(() => Assert.Equal("Page 1 of 3", first.Find("[data-testid='page-status']").TextContent));
        Assert.Equal("/?page=2", first.Find("[data-testid='page-next']").GetAttribute("href"));
        Assert.Empty(first.FindAll("[data-testid='page-previous']"));

        _context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?page=3");
        first.WaitForAssertion(() => Assert.Equal("Page 3 of 3", first.Find("[data-testid='page-status']").TextContent));
        Assert.Equal("/?page=2", first.Find("[data-testid='page-previous']").GetAttribute("href"));
        Assert.Empty(first.FindAll("[data-testid='page-next']"));
    }

    [Fact]
    public void TypingInTheSearchBoxMovesTheAddressOnceTheTypingPauses()
    {
        Arrange();
        _boards.Add(Board("Roadmap", Me, "Ada"));
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='board-search']")));

        page.Find("[data-testid='board-search']").Input("road");

        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        page.WaitForAssertion(() => Assert.Contains("search=road", navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void OwnersMayInviteAndTheInvitationIsSentAsAMember()
    {
        Arrange(role: "Owner");
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='invite-open']")));

        page.Find("[data-testid='invite-open']").Click();
        page.Find("[data-testid='invite-email']").Input("grace@dev.test");
        page.Find("[data-testid='invite-dialog'] form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Invitation sent to grace@dev.test.", page.Find("[data-testid='invite-notice']").TextContent));
        var call = Assert.Single(_authority.Calls, call => call.Path == $"api/v1/organizations/{Studio:D}/invite");
        Assert.Contains("\"role\":\"Member\"", call.Body);
        Assert.Contains("grace@dev.test", call.Body);
    }

    [Fact]
    public void MembersDoNotSeeTheInviteControl()
    {
        Arrange(role: "Member");

        var page = Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='boards-empty']")));
        Assert.Empty(page.FindAll("[data-testid='invite-open']"));
    }

    [Fact]
    public void EscapeClosesTheDialogWithoutSendingAnything()
    {
        Arrange();
        var page = Render();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='empty-create']")));
        page.Find("[data-testid='empty-create']").Click();

        page.Find("[role='dialog']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Escape" });

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[role='dialog']")));
        Assert.DoesNotContain(_boards.Calls, call => call.Method == "POST");
    }


    private void Arrange(string role = "Owner")
    {
        _context.AddAuthorization().SetAuthorized("ada@dev.test");
        _context.Services.AddSingleton<TimeProvider>(new ManualClock(Now));
        _context.Services.AddSingleton<ICurrentSession>(new FakeSession(Principal()));

        _authority.On(HttpMethod.Get, "api/v1/organizations", HttpStatusCode.OK, new[]
        {
            new { id = Studio.ToString("D"), name = "studio", userRole = role, imageUrl = (string?)null },
        });
        _authority.On(HttpMethod.Post, $"api/v1/organizations/{Studio:D}/invite", HttpStatusCode.OK, new { });

        _context.Services.AddSingleton(new AuthorityClient(new HttpClient(_authority) { BaseAddress = new Uri("http://authority.test/") }));
        _context.Services.AddSingleton(new BoardsClient(new HttpClient(_boards) { BaseAddress = new Uri("http://boards-service/") }));
    }

    /// <summary>
    /// Renders the dashboard at the address the arguments name, as the router would: query parameters come from the address, so the
    /// page is reached by navigating there first.
    /// </summary>
    private IRenderedComponent<Home> Render(string? search = null, bool favourites = false, int? page = null)
    {
        _context.Services.GetRequiredService<NavigationManager>().NavigateTo(BoardQuery.Url(search, favourites, page ?? 1));
        return _context.Render<Home>();
    }

    private static BoardSummaryDto Board(string title, Guid author, string authorName, bool favourite = false, int minutesAgo = 0) =>
        new(Guid.NewGuid(), title, author, authorName, ImageSeed: 1, Now.AddMinutes(-minutesAgo), favourite);

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity(
            [new Claim("sub", Me.ToString("D")), new Claim("organization", Studio.ToString("D"))],
            SessionClaims.AuthenticationType));

    private sealed class FakeSession(ClaimsPrincipal principal) : ICurrentSession
    {
        public Task<string?> IdAsync() => Task.FromResult<string?>("session-1");

        public Task<ClaimsPrincipal?> PrincipalAsync() => Task.FromResult<ClaimsPrincipal?>(principal);
    }
}
