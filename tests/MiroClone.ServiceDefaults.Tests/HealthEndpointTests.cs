using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>Health reports; an optional integration that is off is shown as data, and never fails the probe.</summary>
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>());
        builder.AddServiceDefaults();
        builder.Services.AddOptionalIntegration("cache", isEnabled: false);
        _app = builder.Build();
        _app.MapDefaultEndpoints();
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task DisabledOptionalIntegrationIsReportedAsDataAndTheProbeStaysHealthy()
    {
        var report = await _app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Status);
        var entry = report.Entries["optional-integrations"];
        Assert.Equal("disabled", entry.Data["cache"]);
    }

    [Fact]
    public async Task AliveRunsOnlyTheLiveTaggedChecks()
    {
        var report = await _app.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(check => check.Tags.Contains("live"), TestContext.Current.CancellationToken);

        Assert.Contains("self", report.Entries.Keys);
        Assert.DoesNotContain("optional-integrations", report.Entries.Keys);
    }
}
