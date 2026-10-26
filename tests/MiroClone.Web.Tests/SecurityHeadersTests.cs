using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.Web.Account;
using MiroClone.Web.Security;
using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>Every response carries the framing and sniffing headers, and an account form larger than its limit is refused unread (ADR 0047).</summary>
public sealed class SecurityHeadersTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAntiforgery();
        _app = builder.Build();
        _app.UseSecurityHeaders();
        _app.MapGet("/", () => Results.Ok("home"));
        _app.MapAccountEndpoints();

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
    public async Task EveryResponseForbidsFramingAndSniffingAndSetsAReferrerPolicy()
    {
        using var response = await _client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEndpointWithTheLimitRefusesALargerBodyBeforeReadingIt()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.MapGroup("/limited").LimitFormBody(AccountEndpoints.MaximumFormBytes)
            .MapPost("/", async (HttpContext http) => { await http.Request.ReadFormAsync(); return Results.Ok(); });
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestServer().CreateClient();
        using var content = new StringContent($"email={new string('a', AccountEndpoints.MaximumFormBytes * 2)}", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await client.PostAsync("/limited/", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await app.StopAsync(TestContext.Current.CancellationToken);
        await app.DisposeAsync();
    }
}
