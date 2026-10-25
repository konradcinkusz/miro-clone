using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.ServiceDefaults;
using MiroClone.TestSupport;

namespace MiroClone.CanvasService.Tests;

/// <summary>
/// Builds the service's wiring as Program.cs does, on TestServer, so no socket is opened and no test needs the network.
/// Kept in step with Program.cs by review: a change to one is a change to the other.
/// </summary>
internal static class ServiceHost
{
    public static async Task<WebApplication> StartAsync(
        IDictionary<string, string?> settings,
        Action<JwtBearerOptions>? configureBearer = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = TestAuthority.Issuer,
            ["Auth:Audience"] = "miro-clone",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();
        builder.Services.AddOriginGuard(builder.Configuration);
        builder.Services.AddCanvasService(builder.Configuration, configureBearer);

        var app = builder.Build();
        app.UseOriginGuard();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }
}
