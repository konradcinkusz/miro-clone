using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>The origin guard: the three pinned refusals, plus the allowed cases that keep it from breaking the app.</summary>
public sealed class OriginGuardTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>
        {
            ["OriginGuard:AllowedOrigins:0"] = "https://app.test",
        });
        builder.Services.AddOriginGuard(builder.Configuration);
        _app = builder.Build();
        _app.UseOriginGuard();
        _app.MapGet("/probe", () => Results.Ok());
        _app.MapPost("/probe", () => Results.Ok());

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
    public async Task ForeignOriginIsRefusedOnAPOST()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");
        request.Headers.Add("Origin", "https://evil.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ForeignOriginIsRefusedOnAGETBeforeTheEndpointRuns()
    {
        // Stands in for the websocket handshake: a GET carrying a foreign Origin is refused before any upgrade.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("Origin", "https://evil.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CrossSiteRequestWithoutAnOriginIsRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SameOriginRequestIsAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");
        request.Headers.Add("Origin", "http://localhost");

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ConfiguredExtraOriginIsAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");
        request.Headers.Add("Origin", "https://app.test");

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RequestWithoutAnOriginIsAllowed()
    {
        // curl, the server-side HttpClient, health probes and Playwright's request context send no Origin.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TopLevelCrossSiteNavigationIsAllowed()
    {
        // A link followed from another site loads the page; it changes nothing and its response is unreadable to that site.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        request.Headers.Add("Sec-Fetch-Mode", "navigate");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }
}
