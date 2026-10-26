using Xunit;
using Microsoft.Playwright;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Journeys;

[Collection(EstateGroup.Name)]
public sealed class DashboardJourneyTests(EstateFixture stack) : JourneyTests(stack)
{
    [Fact]
    public async Task CreatingABoardOpensItsBoardPageAndListsItOnTheDashboard()
    {
        Stack.RequireRunning();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Create workspace");
        await using var journey = await SignedInAsync(owner);
        var title = Title("Plan");

        var id = await journey.CreateBoardAsync(title);
        Assert.NotEqual(Guid.Empty, id);

        await journey.BackToDashboardAsync();
        await Expect(journey.Card(title)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SearchNarrowsTheBoardListAndAnUnmatchedSearchSaysSo()
    {
        Stack.RequireRunning();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Search workspace");
        await using var journey = await SignedInAsync(owner);
        var wanted = Title("Wanted");
        var other = Title("Other");
        await journey.CreateBoardAsync(wanted);
        await journey.BackToDashboardAsync();
        await journey.CreateBoardAsync(other);
        await journey.BackToDashboardAsync();

        await journey.SearchAsync(wanted);
        await Expect(journey.Card(wanted)).ToBeVisibleAsync();
        await Expect(journey.Card(other)).ToHaveCountAsync(0);

        await journey.SearchAsync(Title("nothing-matches"));
        await Expect(journey.Page.GetByTestId("search-empty")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FavouritingABoardListsItUnderFavouriteBoardsAndUnfavouritingRemovesIt()
    {
        Stack.RequireRunning();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Favourite workspace");
        await using var journey = await SignedInAsync(owner);
        var title = Title("Favourite");
        await journey.CreateBoardAsync(title);
        await journey.BackToDashboardAsync();

        await journey.FavouriteToggle(title).ClickAsync();
        await Expect(journey.FavouriteToggle(title)).ToHaveAttributeAsync("aria-pressed", "true");

        await journey.Page.GetByTestId("nav-favourite-boards").ClickAsync();
        await Expect(journey.Card(title)).ToBeVisibleAsync();

        await journey.FavouriteToggle(title).ClickAsync();
        await Expect(journey.Page.GetByTestId("favourites-empty")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RenamingABoardChangesItsCardAndItsBoardPage()
    {
        Stack.RequireRunning();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Rename workspace");
        await using var journey = await SignedInAsync(owner);
        var title = Title("Before");
        var renamed = Title("After");
        await journey.CreateBoardAsync(title);
        await journey.BackToDashboardAsync();

        await journey.RenameBoardAsync(title, renamed);

        await Expect(journey.Card(renamed)).ToBeVisibleAsync();
        await Expect(journey.Card(title)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DeletingTheLastBoardShowsTheEmptyWorkspace()
    {
        Stack.RequireRunning();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Delete workspace");
        await using var journey = await SignedInAsync(owner);
        var title = Title("Doomed");
        await journey.CreateBoardAsync(title);
        await journey.BackToDashboardAsync();

        await journey.DeleteBoardAsync(title);

        await Expect(journey.Card(title)).ToHaveCountAsync(0);
        await Expect(journey.Page.GetByTestId("boards-empty")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheNewBoardDialogKeepsFocusInsideAndReturnsItOnEscape()
    {
        Stack.RequireRunning();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Focus workspace");
        await using var journey = await SignedInAsync(owner);

        await journey.Page.GetByTestId("empty-create").ClickAsync();
        var dialog = journey.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "New board" });
        await Expect(dialog).ToBeVisibleAsync();

        // Focus is inside the dialog as soon as it opens, and Tab cycles through its controls without leaving it.
        const string insideDialog = "() => document.activeElement !== null && document.activeElement.closest('[role=dialog]') !== null";
        await journey.WaitForScriptAsync<bool>(insideDialog, inside => inside, "Focus inside the new board dialog");
        for (var press = 0; press < 12; press++)
        {
            await journey.Page.Keyboard.PressAsync("Tab");
            await journey.WaitForScriptAsync<bool>(insideDialog, inside => inside, $"Focus after Tab {press + 1}");
        }

        await journey.Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await journey.WaitForScriptAsync<string>(
            "() => document.activeElement?.getAttribute('data-testid') ?? ''",
            testId => testId == "empty-create",
            "The focused element after the dialog closed (expected the Create board opener)");
    }
}
