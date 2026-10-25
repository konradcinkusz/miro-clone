using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace MiroClone.CanvasService.Persistence;

/// <summary>
/// Creates the snapshot schema and rehydrates before the host listens. It runs in <c>StartingAsync</c>, which precedes
/// every hosted service's <c>StartAsync</c>, and it is registered after the kernel's migration, so the schema work runs
/// against an already migrated database (ADR 0009).
/// </summary>
internal sealed class CanvasStoreBootstrap(CanvasRegistry registry) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => registry.InitializeAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Reports whether snapshots are being written. Degraded is still a passing probe (P8): the service serves from memory.</summary>
internal sealed class CanvasStoreHealthCheck(CanvasRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["loadedBoards"] = registry.LoadedBoards };
        var result = registry.IsPersisting
            ? HealthCheckResult.Healthy("Canvas snapshots are persisted.", data)
            : HealthCheckResult.Degraded("Canvas snapshots are not persisting; boards are served from memory only.", data: data);
        return Task.FromResult(result);
    }
}
