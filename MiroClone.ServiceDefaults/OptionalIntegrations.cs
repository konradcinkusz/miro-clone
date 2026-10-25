using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiroClone.ServiceDefaults;

/// <summary>An external integration a service can run without (P8): on, or off and degraded.</summary>
public interface IOptionalIntegration
{
    string Name { get; }

    bool IsEnabled { get; }
}

public static class OptionalIntegrationRegistration
{
    /// <summary>Registers an integration so that /health and the startup banner report its state.</summary>
    public static IServiceCollection AddOptionalIntegration(this IServiceCollection services, string name, bool isEnabled)
    {
        services.AddSingleton<IOptionalIntegration>(new OptionalIntegration(name, isEnabled));
        return services;
    }

    private sealed class OptionalIntegration(string name, bool isEnabled) : IOptionalIntegration
    {
        public string Name { get; } = name;

        public bool IsEnabled { get; } = isEnabled;
    }
}

/// <summary>Reports every optional integration's state as health data. Always healthy: degradation is visible, not fatal.</summary>
internal sealed class OptionalIntegrationsHealthCheck(IEnumerable<IOptionalIntegration> integrations) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        foreach (var integration in integrations)
        {
            data[integration.Name] = integration.IsEnabled ? "enabled" : "disabled";
        }

        return Task.FromResult(HealthCheckResult.Healthy("Optional integrations reported.", data));
    }
}

/// <summary>Prints the same integration list at startup, so the state is visible in the first log lines.</summary>
internal sealed partial class OptionalIntegrationBanner(
    IEnumerable<IOptionalIntegration> integrations,
    ILogger<OptionalIntegrationBanner> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var integration in integrations)
        {
            LogIntegration(logger, integration.Name, integration.IsEnabled ? "enabled" : "disabled");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Optional integration {Name}: {State}")]
    private static partial void LogIntegration(ILogger logger, string name, string state);
}
