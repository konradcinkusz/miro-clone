using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiroClone.ServiceDefaults;
using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>
/// The origin guard is the kernel's middleware (blazor-web-app §8, ADR 0006), run here as the web app runs it: first in the
/// pipeline, with a plain endpoint behind it. A websocket upgrade cannot be made through TestServer, so the refusal of a
/// websocket handshake is pinned by a GET, as ADR 0006 records.
/// </summary>
public sealed class OriginGuardTests : IDisposable
{
    private readonly IHost _host = BuildHost();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task AForeignOriginCannotPost()
    {
        using var client = _host.GetTestClient();
        using var request = Request(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AForeignOriginIsRefusedBeforeAWebsocketUpgrade()
    {
        using var client = _host.GetTestClient();
        using var request = Request(HttpMethod.Get, "/_blazor");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ACrossSiteRequestWithNoOriginIsRefused()
    {
        using var client = _host.GetTestClient();
        using var request = Request(HttpMethod.Post, "/account/sign-in");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ALinkFollowedFromAnotherSiteStillLoads()
    {
        using var client = _host.GetTestClient();
        using var request = Request(HttpMethod.Get, "/");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        request.Headers.Add("Sec-Fetch-Mode", "navigate");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheAppsOwnOriginIsAllowed()
    {
        using var client = _host.GetTestClient();
        using var request = Request(HttpMethod.Post, "/account/sign-in");
        request.Headers.Add("Origin", "http://localhost");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnOriginListedInConfigurationIsAllowed()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["OriginGuard:AllowedOrigins:0"] = "https://app.example" });
        using var client = host.GetTestClient();
        using var request = Request(HttpMethod.Post, "/account/sign-in");
        request.Headers.Add("Origin", "https://app.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ARequestWithNoOriginAtAllIsAllowed()
    {
        using var client = _host.GetTestClient();

        using var response = await client.SendAsync(Request(HttpMethod.Get, "/health"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path) => new(method, path);

    private static IHost BuildHost(IDictionary<string, string?>? settings = null)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings))
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices((context, services) =>
                {
                    services.AddOriginGuard(context.Configuration);
                    services.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseOriginGuard();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/", () => Results.Ok("home"));
                        endpoints.MapGet("/health", () => Results.Ok("ok"));
                        endpoints.MapGet("/_blazor", () => Results.Ok("socket"));
                        endpoints.MapPost("/_blazor/negotiate", () => Results.Ok("negotiated"));
                        endpoints.MapPost("/account/sign-in", () => Results.Ok("posted"));
                    });
                }))
            .Build();
        host.Start();
        return host;
    }
}
