using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MiroClone.ServiceDefaults;

/// <summary>Configuration under the "RateLimiting" section. Fixed windows, no queueing: a rejection is immediate.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per client per window on the "auth" policy, the brute-force surface.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    /// <summary>Requests per user per window on the "api" policy. Anonymous callers are keyed by client address.</summary>
    public int ApiPermitLimit { get; set; } = 120;

    /// <summary>Catch-all for endpoints that carry no policy.</summary>
    public int GlobalPermitLimit { get; set; } = 300;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Reads X-Forwarded-For for the client address. Turn it on only when a proxy in front sets that header;
    /// otherwise the header is client-supplied and every visitor picks their own bucket.
    /// </summary>
    public bool TrustForwardedFor { get; set; }
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Api = "api";
}

public static class RateLimitingExtensions
{
    /// <summary>
    /// Registers the three standard policies. Partitions are the user id, falling back to the client address, so one
    /// NAT or one proxy does not share a single bucket. Call UseRateLimiter after UseAuthentication, so the user is known.
    /// </summary>
    /// <param name="globalLimit">
    /// False leaves out the catch-all limiter, which applies to every request. The web app turns it off: its pages, static
    /// assets and circuit requests would share one budget, and the auth policy is its brute-force control (ADR 0045).
    /// </param>
    public static IServiceCollection AddStandardRateLimiting(this IServiceCollection services, IConfiguration configuration, bool globalLimit = true)
    {
        var settings = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;

            if (globalLimit)
            {
                limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        PartitionKey(context, settings),
                        _ => Window(settings, settings.GlobalPermitLimit)));
            }

            limiter.AddPolicy<string>(RateLimitPolicies.Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context, settings),
                    _ => Window(settings, settings.AuthPermitLimit)));

            limiter.AddPolicy<string>(RateLimitPolicies.Api, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context, settings),
                    _ => Window(settings, settings.ApiPermitLimit)));
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Window(RateLimitingOptions settings, int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = settings.Window,
        QueueLimit = 0,
    };

    private static string PartitionKey(HttpContext context, RateLimitingOptions settings) =>
        WorkspaceClaims.UserId(context.User) is { } userId
            ? $"user:{userId}"
            : ClientKey(context, settings);

    private static string ClientKey(HttpContext context, RateLimitingOptions settings) =>
        $"client:{ClientAddress.Resolve(context, settings.TrustForwardedFor)}";

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window)
            ? (int)Math.Ceiling(window.TotalSeconds)
            : 0;

        var response = context.HttpContext.Response;
        response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        await response.WriteAsJsonAsync(new { error = "rate_limited", retryAfter }, cancellationToken);
    }
}

/// <summary>The one place that decides which address a request came from. Shared by every limiter partition.</summary>
internal static class ClientAddress
{
    private const string ForwardedFor = "X-Forwarded-For";

    public static string Resolve(HttpContext context, bool trustForwardedFor)
    {
        if (trustForwardedFor && context.Request.Headers.TryGetValue(ForwardedFor, out var forwarded))
        {
            // The leftmost entry is the client the edge proxy saw.
            var first = forwarded.ToString().Split(',', StringSplitOptions.TrimEntries)[0];
            if (first.Length > 0)
            {
                return first;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
