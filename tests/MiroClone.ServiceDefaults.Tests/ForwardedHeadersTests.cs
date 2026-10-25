using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>
/// The Fly.io proxy terminates TLS (ADR 0018). The origin guard must see the public https origin, which only the forwarded
/// headers provide, and they must come before the guard in the pipeline.
/// </summary>
public sealed class ForwardedHeadersTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>());
        builder.Services.AddProxyForwardedHeaders();
        builder.Services.AddOriginGuard(builder.Configuration);
        _app = builder.Build();
        _app.UseProxyForwardedHeaders();
        _app.UseOriginGuard();
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
    public async Task PublicHttpsOriginIsAllowedWhenTheProxyForwardsSchemeAndHost()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "miroclone-web-dev.fly.dev");
        request.Headers.Add("Origin", "https://miroclone-web-dev.fly.dev");

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PublicHttpsOriginIsRefusedWithoutTheForwardedHeaders()
    {
        // Without the proxy's headers the app sees http on its own host, so the public origin does not match.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");
        request.Headers.Add("Origin", "https://miroclone-web-dev.fly.dev");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ForwardedOriginFromAForeignSiteIsStillRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "miroclone-web-dev.fly.dev");
        request.Headers.Add("Origin", "https://evil.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }
}
