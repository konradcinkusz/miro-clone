using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiroClone.Web.Account;
using MiroClone.Web.Components.Pages.Account;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>The sign-in page lists the demo logins only in Development with seeding on, and fills the form from them (ADR 0051).</summary>
public sealed class DemoLoginHintTests : IDisposable
{
    private readonly List<BunitContext> _contexts = [];

    public void Dispose() => _contexts.ForEach(context => context.Dispose());

    [Theory]
    [InlineData("Production", "true", false)]
    [InlineData("Staging", "true", false)]
    [InlineData("Development", "false", false)]
    [InlineData("Development", null, false)]
    [InlineData("Development", "true", true)]
    public void TheHintIsRuledByTheEnvironmentAndTheSeedSwitch(string environment, string? seedEnabled, bool visible)
    {
        var page = Render(environment, seedEnabled, interactive: true);

        Assert.Equal(visible, page.FindAll("[data-testid='demo-logins']").Count == 1);
        Assert.Equal(visible ? DemoLogins.All.Count : 0, page.FindAll("[data-testid='demo-login']").Count);
        if (!visible)
        {
            Assert.DoesNotContain(DemoLogins.Password, page.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("dev.test", page.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheHintListsEveryDemoLoginWithItsRoleAndWorkspace()
    {
        var page = Render("Development", "true", interactive: true);

        var emails = page.FindAll("[data-testid='demo-login']").Select(button => button.GetAttribute("data-email")).ToArray();
        Assert.Equal(["owner@dev.test", "admin@dev.test", "member@dev.test", "other@dev.test"], emails);
        Assert.Contains("Admin of Dev Workspace", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Owner of Other Workspace", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ClickingALoginFillsTheFormAndSubmitsNothing()
    {
        var page = Render("Development", "true", interactive: true);

        page.Find("[data-email='admin@dev.test']").Click();

        Assert.Equal("admin@dev.test", page.Find("#sign-in-email").GetAttribute("value"));
        Assert.Equal(DemoLogins.Password, page.Find("#sign-in-password").GetAttribute("value"));
        Assert.Equal("post", page.Find("form").GetAttribute("method"));
    }

    [Fact]
    public void TheFormIsDisabledWhileThePageIsPrerenderedAndEnabledOnceInteractive()
    {
        // The interactive render replaces the prerendered markup, so anything typed before it is lost (ADR 0042, 0051): the
        // prerendered form takes no input at all.
        var prerendered = Render("Development", "true", interactive: false);
        var interactive = Render("Development", "true", interactive: true);

        Assert.True(prerendered.Find("form fieldset").HasAttribute("disabled"));
        Assert.All(prerendered.FindAll("[data-testid='demo-login']"), button => Assert.True(button.Closest("fieldset")!.HasAttribute("disabled")));
        Assert.False(interactive.Find("form fieldset").HasAttribute("disabled"));
    }

    private IRenderedComponent<SignIn> Render(string environment, string? seedEnabled, bool interactive)
    {
        var context = new BunitContext();
        _contexts.Add(context);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(seedEnabled is null ? [] : new Dictionary<string, string?> { ["Seed:Enabled"] = seedEnabled })
            .Build();
        context.Services.AddSingleton(DemoLoginHint.From(configuration, new TestEnvironment { EnvironmentName = environment }));
        context.SetRendererInfo(new RendererInfo("Server", interactive));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/sign-in");
        return context.Render<SignIn>();
    }
}
