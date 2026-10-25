using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace MiroClone.ServiceDefaults;

/// <summary>
/// Forwarded headers from the Fly.io proxy (ADR 0018). TLS ends at the proxy, so a request reaches the app as plain http.
/// The origin guard compares the request's scheme and host with the browser's Origin, so these headers must be applied
/// before it, or every browser request from the public https origin would be refused.
/// </summary>
public static class ForwardedHeadersRegistration
{
    public static IServiceCollection AddProxyForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // One hop. The proxy appends the address it saw, so the last entry is the client. Earlier entries are client-supplied.
            options.ForwardLimit = 1;

            // The proxy's address is not fixed, so the default loopback-only trust is cleared. The app is reachable only through
            // the proxy (public ingress) or the private network, so the last forwarded entry is always the proxy's view.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
        return services;
    }

    /// <summary>Place it first in the pipeline, before <c>UseOriginGuard</c>.</summary>
    public static IApplicationBuilder UseProxyForwardedHeaders(this IApplicationBuilder app) => app.UseForwardedHeaders();
}
