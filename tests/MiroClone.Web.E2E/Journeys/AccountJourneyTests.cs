using System.Text.RegularExpressions;
using Xunit;
using Microsoft.Playwright;
using MiroClone.Web.E2E.Browser;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Journeys;

[Collection(EstateGroup.Name)]
public sealed class AccountJourneyTests(EstateFixture stack) : JourneyTests(stack)
{
    [Fact]
    public async Task SignUpOpensTheDashboardOfAnAccountWithoutAWorkspace()
    {
        Stack.RequireRunning();
        var account = Account.New("signup");
        await using var journey = await Journey.OpenAsync(Stack);

        await journey.GoToStaticAsync("/sign-up");
        await journey.Page.GetByLabel("Email").FillAsync(account.Email);
        await journey.Page.GetByLabel("Password").FillAsync(account.Password);
        await journey.Page.GetByLabel("I accept the Terms of Use").CheckAsync();
        await journey.Page.GetByLabel("I accept the Privacy Policy").CheckAsync();
        await journey.Page.GetByTestId("sign-up-submit").ClickAsync();

        // The dev outbox sends no verification step (ADR 0011), so the new account is signed in at once and has no organization yet.
        await Expect(journey.Page.GetByTestId("signed-in-email")).ToHaveTextAsync(account.Email);
        await Expect(journey.Page.GetByTestId("workspace-none")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SignOutEndsTheSessionSoTheDashboardAsksForSignInAgain()
    {
        Stack.RequireRunning();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Sign-out workspace");
        await using var journey = await SignedInAsync(owner);

        await journey.Page.GetByTestId("sign-out-link").ClickAsync();
        await journey.Page.GetByTestId("sign-out-submit").ClickAsync();
        await Expect(journey.Page.GetByTestId("sign-in-form")).ToBeVisibleAsync();

        await journey.GoToStaticAsync("/");
        await Expect(journey.Page).ToHaveURLAsync(new Regex("/sign-in"));
        await Expect(journey.Page.GetByTestId("sign-in-form")).ToBeVisibleAsync();
    }
}
