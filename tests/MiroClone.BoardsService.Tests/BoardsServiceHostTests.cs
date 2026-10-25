using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace MiroClone.BoardsService.Tests;

/// <summary>The service starts, answers the probe and its guard refuses a foreign origin. Migrations are asserted in the PostgreSQL-gated tests.</summary>
public sealed class BoardsServiceHostTests : IAsyncLifetime
{
    private WebApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await ServiceHost.StartAsync(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "InMemory",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task HealthProbeAnswersOk()
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ForeignOriginIsRefusedByTheOriginGuard()
    {
        using var client = _app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
