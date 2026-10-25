using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.BoardsService.Boards;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// Builds the service's wiring as Program.cs does, on TestServer, so no socket is opened and no test needs the network.
/// Kept in step with Program.cs by review: a change to one is a change to the other.
/// </summary>
internal static class ServiceHost
{
    /// <summary>The audience appsettings.json configures for this service.</summary>
    public const string Audience = "miro-clone-boards";

    /// <summary>Stands in for authservice; the test authority answers its discovery document at this issuer.</summary>
    public const string Authority = TestAuthority.Issuer;

    /// <summary>The canvas base address the purge client uses in tests. Its requests are answered by a fake, not by a network.</summary>
    public const string CanvasBaseUrl = "http://canvas.test/";

    /// <param name="settings">Overrides on top of the test defaults (authority, audience, canvas address).</param>
    /// <param name="authorityBackchannel">Where discovery and JWKS are fetched from, so token validation runs for real.</param>
    /// <param name="configureServices">Last word on registrations, used to replace the canvas purge's handler.</param>
    public static async Task<WebApplication> StartAsync(
        IDictionary<string, string?> settings,
        HttpMessageHandler? authorityBackchannel = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = Authority,
            ["Auth:Audience"] = Audience,
            ["Canvas:BaseUrl"] = CanvasBaseUrl,
        });
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddServiceDefaults();
        builder.Services.AddOriginGuard(builder.Configuration);
        builder.Services.AddJwtValidation(builder.Configuration, options =>
        {
            if (authorityBackchannel is not null)
            {
                options.BackchannelHttpHandler = authorityBackchannel;
            }
        });
        builder.Services.AddStandardRateLimiting(builder.Configuration);
        builder.Services.AddBoardsService(builder.Configuration);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseOriginGuard();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapDefaultEndpoints();
        app.MapBoardEndpoints();
        await app.StartAsync();
        return app;
    }
}
