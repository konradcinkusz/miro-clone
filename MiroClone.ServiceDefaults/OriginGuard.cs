using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MiroClone.ServiceDefaults;

/// <summary>Configuration under the "OriginGuard" section.</summary>
public sealed class OriginGuardOptions
{
    public const string SectionName = "OriginGuard";

    /// <summary>
    /// Origins allowed besides the app's own (scheme://host[:port]). Empty means same-origin only.
    /// Forwarded headers must be configured before the guard, so the app sees its public host.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Refuses browser requests from foreign origins. A websocket handshake is not subject to CORS, so a foreign page
/// could otherwise open a Blazor circuit or a canvas hub. This is a mitigation, not authentication: it does not stop
/// code that already runs on the machine or a non-browser client that sends neither header. CORS stays unconfigured.
/// </summary>
public static class OriginGuardRegistration
{
    public static IServiceCollection AddOriginGuard(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OriginGuardOptions>().Bind(configuration.GetSection(OriginGuardOptions.SectionName));
        return services;
    }

    /// <summary>Place it first in the pipeline, before antiforgery and endpoints.</summary>
    public static IApplicationBuilder UseOriginGuard(this IApplicationBuilder app) => app.UseMiddleware<OriginGuardMiddleware>();
}

internal sealed class OriginGuardMiddleware(RequestDelegate next, IOptions<OriginGuardOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!OriginPolicy.IsAllowed(context.Request, options.Value.AllowedOrigins))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}

internal static class OriginPolicy
{
    public static bool IsAllowed(HttpRequest request, IReadOnlyCollection<string> extraOrigins)
    {
        // A cross-site request is refused unless it is a top-level navigation, which changes nothing and whose
        // response the other site cannot read.
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase) && !IsTopLevelNavigation(request))
        {
            return false;
        }

        // No Origin (curl, server-side HttpClient, health probes, Playwright's request context) is allowed.
        var origin = request.Headers["Origin"].ToString();
        if (origin.Length == 0)
        {
            return true;
        }

        return string.Equals(origin, $"{request.Scheme}://{request.Host.Value}", StringComparison.OrdinalIgnoreCase)
            || extraOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTopLevelNavigation(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && string.Equals(request.Headers["Sec-Fetch-Mode"].ToString(), "navigate", StringComparison.OrdinalIgnoreCase)
        && string.Equals(request.Headers["Sec-Fetch-Dest"].ToString(), "document", StringComparison.OrdinalIgnoreCase);
}
