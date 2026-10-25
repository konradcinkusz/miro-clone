using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.Web.Components.Pages.Account;
using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>The sign-in page renders the state its query carries: the message, the success notices, and a safe return path.</summary>
public sealed class AccountPageTests : IDisposable
{
    private readonly BunitContext _context = new();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void AnErrorFromTheEndpointIsShownOnceInAnAlert()
    {
        var page = Visit("error=" + Uri.EscapeDataString("Invalid email or password"));

        Assert.Equal("Invalid email or password", page.Find("[role='alert']").TextContent);
        Assert.Single(page.FindAll("[role='alert']"));
    }

    [Fact]
    public void TheFormPostsToTheAccountEndpointWithTheSafeReturnPath()
    {
        var page = Visit("returnUrl=" + Uri.EscapeDataString("//evil.example"));

        Assert.Equal("/account/sign-in", page.Find("form").GetAttribute("action"));
        Assert.Equal("/", page.Find("input[name='returnUrl']").GetAttribute("value"));
    }

    [Fact]
    public void TheVerifiedNoticeIsShownAsStatus()
    {
        var page = Visit("verified=1");

        Assert.Contains("verified", page.Find("[role='status']").TextContent);
    }

    private IRenderedComponent<SignIn> Visit(string query)
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("http://localhost/sign-in?" + query);
        return _context.Render<SignIn>();
    }
}
