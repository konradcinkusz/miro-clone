using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.BoardsService.Boards;
using MiroClone.ServiceDefaults;
using MiroClone.TestSupport;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// Builds the service's wiring as Program.cs does, on TestServer, so no socket is opened and no test needs the network.
/// Kept in step with Program.cs by review: a change to one is a change to the other.
/// The authority settings point at <see cref="TestAuthority"/>; a test that needs the JWKS passes its backchannel.
/// </summary>
internal static class ServiceHost
{
    /// <summary>The audience every estate token carries (ADR 0011), and the one appsettings.json configures.</summary>
    public const string Audience = "miro-clone";

    /// <summary>The canvas base address the purge client uses in tests. Its requests are answered by a fake, not by a network.</summary>
    public const string CanvasBaseUrl = "http://canvas.test/";

    /// <param name="settings">Overrides on top of the test defaults (authority, audience, canvas address).</param>
    /// <param name="configureBearer">Where discovery and JWKS are fetched from, so token validation runs for real.</param>
    /// <param name="configureServices">Last word on registrations, used to replace the canvas purge's handler.</param>
    public static async Task<WebApplication> StartAsync(
        IDictionary<string, string?> settings,
        Action<JwtBearerOptions>? configureBearer = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = TestAuthority.Issuer,
            ["Auth:Audience"] = Audience,
            ["Canvas:BaseUrl"] = CanvasBaseUrl,
        });
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddServiceDefaults();
        builder.Services.AddProxyForwardedHeaders();
        builder.Services.AddOriginGuard(builder.Configuration);
        builder.Services.AddStandardRateLimiting(builder.Configuration);
        builder.Services.AddBoardsService(builder.Configuration, configureBearer);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseProxyForwardedHeaders();
        app.UseOriginGuard();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapDefaultEndpoints();
        app.MapSessionEndpoints();
        app.MapBoardEndpoints();
        await app.StartAsync();
        return app;
    }
}
