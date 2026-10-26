using Xunit;
using Microsoft.Playwright;
using MiroClone.Web.E2E.Browser;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Journeys;

/// <summary>The checks only a real browser can make: the layout at phone width, and the token contract under each colour scheme.</summary>
[Collection(EstateGroup.Name)]
public sealed class LayoutJourneyTests(EstateFixture stack) : JourneyTests(stack)
{
    private static readonly ViewportSize Phone = new() { Width = 390, Height = 844 };

    [Fact]
    public async Task TheSignInPageFitsAPhoneWidthWithoutHorizontalScroll()
    {
        Stack.RequireRunning();
        await using var journey = await Journey.OpenAsync(Stack, Phone);

        await journey.GoToStaticAsync("/sign-in");

        await Expect(journey.Page.GetByTestId("sign-in-form")).ToBeVisibleAsync();
        Assert.Equal(0, await OverflowAsync(journey));
    }

    [Fact]
    public async Task TheDashboardFitsAPhoneWidthWithoutHorizontalScroll()
    {
        await Stack.RequireAuthServiceAsync();
        var (owner, _) = await Authority.CreateWorkspaceOwnerAsync("Phone workspace");
        await using var journey = await SignedInAsync(owner, Phone);

        await Expect(journey.Page.GetByTestId("dashboard-heading")).ToBeVisibleAsync();
        Assert.Equal(0, await OverflowAsync(journey));
    }

    [Fact]
    public async Task TheDarkColourSchemeAppliesTheDarkTokens()
    {
        Stack.RequireRunning();
        await using var journey = await Journey.OpenAsync(Stack, colourScheme: ColorScheme.Dark);

        await journey.GoToStaticAsync("/sign-in");

        await Expect(journey.Page.GetByTestId("sign-in-form")).ToBeVisibleAsync();
        Assert.Equal("rgb(20, 22, 28)", await ComputedAsync(journey, "backgroundColor"));
        Assert.Equal("rgb(236, 238, 243)", await ComputedAsync(journey, "color"));
    }

    [Fact]
    public async Task TheLightColourSchemeAppliesTheLightTokens()
    {
        Stack.RequireRunning();
        await using var journey = await Journey.OpenAsync(Stack, colourScheme: ColorScheme.Light);

        await journey.GoToStaticAsync("/sign-in");

        await Expect(journey.Page.GetByTestId("sign-in-form")).ToBeVisibleAsync();
        Assert.Equal("rgb(255, 255, 255)", await ComputedAsync(journey, "backgroundColor"));
    }

    /// <summary>Pixels the document is wider than its viewport. Zero means no horizontal scroll.</summary>
    private static Task<int> OverflowAsync(Journey journey) =>
        journey.Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

    private static Task<string> ComputedAsync(Journey journey, string property) =>
        journey.Page.EvaluateAsync<string>($"() => getComputedStyle(document.body).{property}");
}
