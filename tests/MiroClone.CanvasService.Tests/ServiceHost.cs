using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiroClone.CanvasService.Api;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Persistence;
using MiroClone.CanvasService.Realtime;
using MiroClone.ServiceDefaults;

namespace MiroClone.CanvasService.Tests;

/// <summary>
/// Builds the service's wiring as Program.cs does, on TestServer, so no socket is opened and no test needs the network.
/// Kept in step with Program.cs by review: a change to one is a change to the other. Authentication uses the test key.
/// BoardsService and the snapshot store are fakes (<see cref="FakeBoardAccess"/>, <see cref="FakeSnapshotStore"/>) unless a
/// test asks for the real store, which needs a relational database such as SQLite.
/// </summary>
internal static class ServiceHost
{
    public static async Task<WebApplication> StartAsync(
        IDictionary<string, string?> settings,
        bool realSnapshotStore = false,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = TestTokens.Authority,
            ["Auth:Issuer"] = TestTokens.Authority,
            ["Auth:Audience"] = TestTokens.Audience,
            ["Database:Provider"] = "InMemory",
            ["Canvas:FlushInterval"] = "00:01:00",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();
        builder.Services.AddOriginGuard(builder.Configuration);
        builder.Services.AddCanvasService(builder.Configuration, TestTokens.ConfigureBearer);

        builder.Services.AddSingleton<FakeBoardAccess>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IBoardAccess>(sp => sp.GetRequiredService<FakeBoardAccess>()));

        if (!realSnapshotStore)
        {
            builder.Services.AddSingleton<FakeSnapshotStore>();
            builder.Services.Replace(ServiceDescriptor.Singleton<ICanvasSnapshotStore>(sp => sp.GetRequiredService<FakeSnapshotStore>()));
        }

        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseOriginGuard();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapDefaultEndpoints();
        app.MapCanvasEndpoints();
        app.MapHub<CanvasHub>(CanvasHubMessages.HubPath);
        await app.StartAsync();
        return app;
    }

    /// <summary>Settings for a host backed by a SQLite file, so a restart can be simulated against the same database.</summary>
    public static IDictionary<string, string?> SqliteFile(string path) => new Dictionary<string, string?>
    {
        ["Database:Provider"] = "Sqlite",
        ["ConnectionStrings:canvas"] = $"Data Source={path}",
    };
}
