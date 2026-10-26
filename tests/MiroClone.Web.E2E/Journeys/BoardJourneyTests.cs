using Xunit;
using Microsoft.Playwright;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Journeys;

[Collection(EstateGroup.Name)]
public sealed class BoardJourneyTests(EstateFixture stack) : JourneyTests(stack)
{
    [Fact]
    public async Task ShapesDrawnOnABoardAreStillThereAfterAReload()
    {
        await Stack.RequireAuthServiceAsync();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Reload workspace");
        await using var journey = await SignedInAsync(owner);
        await journey.CreateBoardAsync(Title("Persisted"));

        await journey.DrawRectangleAsync(120, 120, 300, 220);
        await Expect(journey.Page.GetByTestId("layer-rectangle")).ToHaveCountAsync(1);

        await journey.ReloadInteractiveAsync();

        await Expect(journey.Page.GetByTestId("board-title")).ToBeVisibleAsync();
        await Expect(journey.Page.GetByTestId("layer-rectangle")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ASecondPersonSeesTheShapesAndTheCursorOfTheFirstLive()
    {
        await Stack.RequireAuthServiceAsync();
        var (owner, organization) = await Authority.CreateWorkspaceOwnerAsync("Live workspace");
        var member = await Authority.AddMemberAsync(owner, organization);
        await using var first = await SignedInAsync(owner);
        await using var second = await SignedInAsync(member);
        var id = await first.CreateBoardAsync(Title("Shared"));
        await second.GoToInteractiveAsync($"/board/{id:D}");
        await Expect(second.Page.GetByTestId("board-title")).ToBeVisibleAsync();

        await first.DrawRectangleAsync(140, 140, 320, 260);

        await Expect(second.Page.GetByTestId("layer-rectangle")).ToHaveCountAsync(1);

        await first.MovePointerOverCanvasAsync();

        await Expect(second.Page.GetByTestId("remote-cursor")).ToHaveCountAsync(1);
        // The banner is shown only while the connection is not live, so its absence is the live check.
        await Expect(first.Page.GetByTestId("connection-banner")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ABoardOfAnotherWorkspaceAnswersBoardNotFound()
    {
        await Stack.RequireAuthServiceAsync();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Owner workspace");
        var (outsider, _) = await Authority.CreateWorkspaceOwnerAsync("Outside workspace");
        await using var insider = await SignedInAsync(owner);
        await using var stranger = await SignedInAsync(outsider);
        var id = await insider.CreateBoardAsync(Title("Private"));

        await stranger.GoToInteractiveAsync($"/board/{id:D}");

        await Expect(stranger.Page.GetByTestId("board-not-found")).ToBeVisibleAsync();
        await Expect(stranger.Page.GetByTestId("board-title")).ToHaveCountAsync(0);
    }
}
