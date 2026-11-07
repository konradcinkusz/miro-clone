using Xunit;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Journeys;

/// <summary>
/// The demo logins the seed creates (ADR 0050, README "Run it locally"): the owner and the member of the Dev Workspace, and the
/// demo boards. They run against an estate the seed has run on (compose, or the AppHost in Development), which CI sets with
/// MIROCLONE_E2E_SEEDED=1. The estate the suite starts by itself does not run the seed, so there they are skipped with that reason.
/// </summary>
[Collection(EstateGroup.Name)]
public sealed class SeededJourneyTests(EstateFixture stack) : JourneyTests(stack)
{
    // The documented development values (they are published in the README, not secrets). gitleaks:allow
    private static readonly Account Owner = new("owner@dev.test", "Dev-Password-123!"); // gitleaks:allow
    private static readonly Account Member = new("member@dev.test", "Dev-Password-123!"); // gitleaks:allow

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

    private async Task RequireSeededAsync()
    {
        await Stack.RequireAuthServiceAsync();
        if (Environment.GetEnvironmentVariable("MIROCLONE_E2E_SEEDED") != "1")
        {
            Assert.Skip("The seed has not run on this estate. CI sets MIROCLONE_E2E_SEEDED=1 for compose and the AppHost, which run it.");
        }
    }
}
