using Xunit;
using Microsoft.Playwright;
using MiroClone.Web.E2E.Browser;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Journeys;

/// <summary>
/// The account forms in a real browser, without signing in (so no authservice account is needed). The server renders each form, then
/// the circuit connects. ADR 0042: before the fix the circuit answered 'Page not found' over the form, and only a browser sees that.
/// </summary>
[Collection(EstateGroup.Name)]
public sealed class AccountPageJourneyTests(EstateFixture stack) : JourneyTests(stack)
{
    [Fact]
    public async Task TheSignUpFormStillShowsAfterTheCircuitConnects()
    {
        Stack.RequireRunning();
        await using var journey = await Journey.OpenAsync(Stack);

        await journey.GoToInteractiveAsync("/sign-up");

        // The form itself needs authservice (the consent versions are read from it), so this journey checks the heading and the
        // absence of the router's 'Page not found', which is the defect. The form is covered by the authenticated journeys.
        await Expect(journey.Page.GetByRole(AriaRole.Heading, new() { Name = "Create an account" })).ToBeVisibleAsync();
        await Expect(journey.Page.GetByText("Page not found")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheSignInFormStillShowsAfterTheCircuitConnects()
    {
        Stack.RequireRunning();
        await using var journey = await Journey.OpenAsync(Stack);

        await journey.GoToInteractiveAsync("/sign-in");

        await Expect(journey.Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        await Expect(journey.Page.GetByTestId("sign-in-form")).ToBeVisibleAsync();
        await Expect(journey.Page.GetByText("Page not found")).ToHaveCountAsync(0);
    }
}
