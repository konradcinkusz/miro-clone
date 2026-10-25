using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MiroClone.ServiceDefaults;

/// <summary>
/// The shared plumbing every service opts into with one call (P2, P9). Each method is an extension,
/// so a service reads its wiring as a list in Program.cs.
/// </summary>
public static class ServiceDefaultsExtensions
{
    private const string LiveTag = "live";

    /// <summary>Telemetry, health, service discovery and the one standard resilience handler.</summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Exactly one resilience handler per HttpClient. A second one multiplies attempts: do not add another.
            http.AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                // The breaker's sampling window must be at least twice the attempt timeout.
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });
            http.AddServiceDiscovery();
        });

        builder.Services.AddHostedService<OptionalIntegrationBanner>();
        return builder;
    }

    /// <summary>Maps /health (readiness, every check) and /alive (liveness, "live"-tagged checks only).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health").DisableRateLimiting();
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains(LiveTag) })
            .DisableRateLimiting();
        return app;
    }

    /// <summary>
    /// OpenTelemetry for logs, traces and metrics (P15). The OTLP exporter is added only when
    /// OTEL_EXPORTER_OTLP_ENDPOINT is set, so a laptop run does not export into nothing.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsProbe(context.Request.Path))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// The kernel's own checks. An optional integration that is off is reported, never failed (P8).
    /// </summary>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag])
            .AddCheck<OptionalIntegrationsHealthCheck>("optional-integrations");
        return builder;
    }

    private static bool IsProbe(PathString path) => path.StartsWithSegments("/health") || path.StartsWithSegments("/alive");
}
