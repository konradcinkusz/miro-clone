using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>The standard policies: uniform 429 body, and client resolution that trusts a forwarded header only when configured.</summary>
public sealed class RateLimitingTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => await Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            _client.Dispose();
        }

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private async Task StartAsync(Dictionary<string, string?> settings)
    {
        var builder = TestApps.NewBuilder(settings);
        builder.AddServiceDefaults();
        builder.Services.AddStandardRateLimiting(builder.Configuration);
        _app = builder.Build();
        _app.UseRateLimiter();
        _app.MapPost("/auth-probe", () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.Auth);
        _app.MapGet("/api-probe", () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.Api);
        _app.MapDefaultEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestServer().CreateClient();
    }

    [Fact]
    public async Task AuthPolicyRejectsPastItsLimitWithTheUniformBodyAndRetryAfter()
    {
        await StartAsync(new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "2" });

        var first = await _client.PostAsync("/auth-probe", null, TestContext.Current.CancellationToken);
        var second = await _client.PostAsync("/auth-probe", null, TestContext.Current.CancellationToken);
        var third = await _client.PostAsync("/auth-probe", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.RetryAfter is not null);

        using var body = JsonDocument.Parse(await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("rate_limited", body.RootElement.GetProperty("error").GetString());
        Assert.True(body.RootElement.GetProperty("retryAfter").GetInt32() > 0);
    }

    [Fact]
    public async Task ForwardedForHeaderIsIgnoredUnlessConfigured()
    {
        await StartAsync(new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "1" });

        var responses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth-probe");
            request.Headers.Add("X-Forwarded-For", $"10.0.0.{attempt}");
            responses.Add((await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
        }

        // A client that rotates the header must not get a fresh bucket.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], responses);
    }

    [Fact]
    public async Task ForwardedForHeaderSplitsBucketsWhenAProxyIsTrusted()
    {
        await StartAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPermitLimit"] = "1",
            ["RateLimiting:TrustForwardedFor"] = "true",
        });

        var responses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth-probe");
            request.Headers.Add("X-Forwarded-For", $"10.0.0.{attempt}");
            responses.Add((await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], responses);
    }

    [Fact]
    public async Task HealthEndpointsAreExemptFromTheGlobalLimit()
    {
        await StartAsync(new Dictionary<string, string?> { ["RateLimiting:GlobalPermitLimit"] = "1" });

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            statuses.Add((await _client.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }
}
