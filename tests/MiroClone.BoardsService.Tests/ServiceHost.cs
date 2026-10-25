using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// Builds the service's wiring as Program.cs does, on TestServer, so no socket is opened and no test needs the network.
/// Kept in step with Program.cs by review: a change to one is a change to the other.
/// </summary>
internal static class ServiceHost
{
    public static async Task<WebApplication> StartAsync(IDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();
        builder.Services.AddOriginGuard(builder.Configuration);
        builder.Services.AddBoardsService(builder.Configuration);

        var app = builder.Build();
        app.UseOriginGuard();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }
}
