using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.ServiceDefaults;
using MiroClone.Web.Account;
using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>
/// The auth rate limit on the account forms (ADR 0045). A rejected form goes back to its page with a message, not a JSON 429, and
/// sign-out is never limited. The host is the same shape as Program.cs: the account group with the policy, and no authority behind it.
/// </summary>
public sealed class AccountRateLimitingTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private int _reached;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["RateLimiting:AuthPermitLimit"] = "3";
        builder.Services.AddAccountRateLimiting(builder.Configuration);
        _app = builder.Build();
        _app.UseRateLimiter();

        var account = _app.MapGroup("/account").RequireRateLimiting(RateLimitPolicies.Auth);
        account.MapPost("/sign-in", () =>
        {
            _reached++;
            return Results.Ok();
        });
        account.MapPost("/sign-out", () => Results.Ok()).DisableRateLimiting();

        await _app.StartAsync();
        _client = _app.GetTestServer().CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task ARejectedSignInGoesBackToTheSignInPageWithTheMessage()
    {
        for (var i = 0; i < 3; i++)
        {
            using var allowed = await _client.PostAsync("/account/sign-in", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await _client.PostAsync("/account/sign-in", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, rejected.StatusCode);
        Assert.Equal(AccountRateLimiting.RejectedPage("/sign-in"), rejected.Headers.Location?.ToString());
        Assert.Equal(3, _reached);
    }

    [Fact]
    public async Task SignOutIsNeverLimited()
    {
        for (var i = 0; i < 10; i++)
        {
            using var response = await _client.PostAsync("/account/sign-out", null, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public void TheRejectionMessageNamesTheAttemptsAndTheWaitInPlainWords()
    {
        Assert.Equal("Too many attempts. Wait a minute, then try again.", AccountRateLimiting.TooManyAttempts);
        Assert.StartsWith("/sign-up?error=", AccountRateLimiting.RejectedPage("/sign-up"), StringComparison.Ordinal);
    }
}
