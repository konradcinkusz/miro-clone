using Xunit;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Journeys;

/// <summary>
/// The demo logins the seed creates (ADR 0050 and 0051, README "Run it locally"): the owner, admin and member of the Dev Workspace,
/// the owner of the Other Workspace, and the demo boards. They run against an estate the seed has run on (compose, or the AppHost in Development), which CI sets with
/// MIROCLONE_E2E_SEEDED=1. The estate the suite starts by itself does not run the seed, so there they are skipped with that reason.
/// </summary>
[Collection(EstateGroup.Name)]
public sealed class SeededJourneyTests(EstateFixture stack) : JourneyTests(stack)
{
    // The documented development values (they are published in the README, not secrets). gitleaks:allow
    private static readonly Account Owner = new("owner@dev.test", "Dev-Password-123!"); // gitleaks:allow
    private static readonly Account Member = new("member@dev.test", "Dev-Password-123!"); // gitleaks:allow
    private static readonly Account Admin = new("admin@dev.test", "Dev-Password-123!"); // gitleaks:allow
    private static readonly Account Other = new("other@dev.test", "Dev-Password-123!"); // gitleaks:allow

    [Fact]
    public async Task TheSeededOwnerSignsInAndSeesTheDemoBoards()
    {
        await RequireSeededAsync();
        await using var journey = await SignedInAsync(Owner);

        await Expect(journey.Card("Demo: Welcome")).ToBeVisibleAsync();
        await Expect(journey.Card("Demo: Sprint planning")).ToBeVisibleAsync();
        await Expect(journey.Card("Demo: Empty board")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheSeededMemberSeesTheOwnersBoardsWithTheirShapes()
    {
        await RequireSeededAsync();
        await using var journey = await SignedInAsync(Member);

        await Expect(journey.Card("Demo: Welcome")).ToBeVisibleAsync();
        await journey.Card("Demo: Welcome").GetByRole(Microsoft.Playwright.AriaRole.Link).First.ClickAsync();

        await Expect(journey.Page.GetByTestId("board-title")).ToBeVisibleAsync();
        // Demo: Welcome holds one rectangle, one ellipse, one note and one text, put there by the seed through the hub.
        await Expect(journey.Page.GetByTestId("layer-rectangle")).ToHaveCountAsync(1);
        await Expect(journey.Page.GetByTestId("layer-ellipse")).ToHaveCountAsync(1);
        await Expect(journey.Page.GetByTestId("layer-note")).ToHaveCountAsync(1);
        await Expect(journey.Page.GetByTestId("layer-text")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task TheSeededMemberSeesTheSeededOwnersEditsLive()
    {
        await RequireSeededAsync();
        await using var owner = await SignedInAsync(Owner);
        await using var member = await SignedInAsync(Member);
        await owner.Card("Demo: Empty board").GetByRole(Microsoft.Playwright.AriaRole.Link).First.ClickAsync();
        await Expect(owner.Page.GetByTestId("board-title")).ToBeVisibleAsync();
        await member.Card("Demo: Empty board").GetByRole(Microsoft.Playwright.AriaRole.Link).First.ClickAsync();
        await Expect(member.Page.GetByTestId("board-title")).ToBeVisibleAsync();
        var before = await member.Page.GetByTestId("layer-rectangle").CountAsync();

        await owner.DrawRectangleAsync(140, 140, 320, 260);

        await Expect(member.Page.GetByTestId("layer-rectangle")).ToHaveCountAsync(before + 1);
        // The estate is ephemeral in CI. The rectangle stays on the demo board, and a repeat seed leaves it alone.
    }

    [Fact]
    public async Task TheSignInPageHintFillsTheFormAndTheAdminSignsInWithIt()
    {
        await RequireSeededAsync();
        await using var journey = await MiroClone.Web.E2E.Browser.Journey.OpenAsync(Stack);
        await journey.GoToInteractiveAsync("/sign-in");
        await journey.WaitForSignInFormAsync();

        // The hint lists every demo login, and one click fills the form (it submits nothing).
        await Expect(journey.Page.GetByTestId("demo-logins")).ToBeVisibleAsync();
        await Expect(journey.Page.GetByTestId("demo-login")).ToHaveCountAsync(4);
        await journey.Page.Locator("[data-testid='demo-login'][data-email='admin@dev.test']").ClickAsync();
        await Expect(journey.Page.Locator("#sign-in-email")).ToHaveValueAsync(Admin.Email);
        await Expect(journey.Page.Locator("#sign-in-password")).ToHaveValueAsync(Admin.Password);
        await Expect(journey.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/sign-in$"));

        await journey.Page.GetByTestId("sign-in-submit").ClickAsync();

        await Expect(journey.Page.GetByTestId("signed-in-email")).ToHaveTextAsync(Admin.Email);
        await Expect(journey.Card("Demo: Welcome")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheSeededAdminSeesTheDevWorkspaceBoardsAndNotTheOtherWorkspacesBoard()
    {
        await RequireSeededAsync();
        await using var journey = await SignedInAsync(Admin);

        await Expect(journey.Card("Demo: Welcome")).ToBeVisibleAsync();
        await Expect(journey.Card("Demo: Other workspace board")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheOtherWorkspaceAndTheDevWorkspaceCannotOpenEachOthersBoards()
    {
        await RequireSeededAsync();

        // One browser at a time: the journeys sign in one after the other, so the slower AppHost estate never serves two sign-ins that overlap.
        Guid otherBoard, devBoard;
        await using (var other = await SignedInAsync(Other))
        {
            await Expect(other.Card("Demo: Other workspace board")).ToBeVisibleAsync();
            await Expect(other.Card("Demo: Welcome")).ToHaveCountAsync(0);
            await other.Card("Demo: Other workspace board").GetByRole(Microsoft.Playwright.AriaRole.Link).First.ClickAsync();
            await Expect(other.Page.GetByTestId("board-title")).ToBeVisibleAsync();
            otherBoard = other.BoardId();
        }

        await using (var owner = await SignedInAsync(Owner))
        {
            await owner.Card("Demo: Welcome").GetByRole(Microsoft.Playwright.AriaRole.Link).First.ClickAsync();
            await Expect(owner.Page.GetByTestId("board-title")).ToBeVisibleAsync();
            devBoard = owner.BoardId();

            await owner.GoToInteractiveAsync($"/board/{otherBoard}");
            await Expect(owner.Page.GetByTestId("board-not-found")).ToBeVisibleAsync();
        }

        await using (var other = await SignedInAsync(Other))
        {
            await other.GoToInteractiveAsync($"/board/{devBoard}");
            await Expect(other.Page.GetByTestId("board-not-found")).ToBeVisibleAsync();
        }
    }

    private async Task RequireSeededAsync()
    {
        await Stack.RequireAuthServiceAsync();
        if (Environment.GetEnvironmentVariable("MIROCLONE_E2E_SEEDED") != "1")
        {
            Assert.Skip("The seed has not run on this estate. CI sets MIROCLONE_E2E_SEEDED=1 for compose and the AppHost, which run it.");
        }
    }
}
