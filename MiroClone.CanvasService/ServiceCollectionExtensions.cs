using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Persistence;
using MiroClone.CanvasService.Realtime;
using MiroClone.ServiceDefaults;

namespace MiroClone.CanvasService;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The canvas service: the "canvas" database, the snapshot store and registry, the realtime hub and the BoardsService
    /// access client. Registration order matters: the kernel's migration is added before the snapshot bootstrap, so the
    /// schema work runs after the migration in <c>StartingAsync</c> (ADR 0009).
    /// </summary>
    /// <param name="configureBearer">Optional hook for tests, for example to supply a signing key without an authority.</param>
    public static IServiceCollection AddCanvasService(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configureBearer = null)
    {
        services.AddDatabaseContext<CanvasDbContext>(configuration, "canvas");
        services.AddMigrateOnStartup<CanvasDbContext>();

        services.AddJwtValidation(configuration, options =>
        {
            options.Events = new JwtBearerEvents { OnMessageReceived = ReadHubTokenFromQuery };
            configureBearer?.Invoke(options);
        });

        services.AddOptions<CanvasOptions>()
            .Bind(configuration.GetSection(CanvasOptions.SectionName))
            .Validate(
                options => options.FlushInterval >= TimeSpan.FromMilliseconds(100) && options.RehydrateLimit is >= 0 and <= 10_000,
                "Canvas:FlushInterval must be at least 100 ms and Canvas:RehydrateLimit between 0 and 10000.")
            .ValidateOnStart();

        services.AddSingleton<ICanvasSnapshotStore, CanvasSnapshotStore>();
        services.AddSingleton<CanvasRegistry>();
        services.AddSingleton<PresenceTracker>();
        services.AddHostedService<CanvasStoreBootstrap>();
        services.AddHostedService<CanvasSnapshotWriter>();

        services.AddHttpClient<IBoardAccess, BoardsAccessClient>(client =>
            {
                client.BaseAddress = new Uri(configuration["Boards:BaseAddress"] ?? "https+http://boards-service/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddHealthChecks().AddCheck<CanvasStoreHealthCheck>("canvas-store");
        services.AddSignalR(hub =>
            {
                // The default 32 KB cap rejects a legal pencil stroke: MaxPathPoints vertices serialise to roughly 40 KB.
                hub.MaximumReceiveMessageSize = MaxHubMessageBytes;
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddStandardRateLimiting(configuration);
        return services;
    }

    private const long MaxHubMessageBytes = 128 * 1024;

    /// <summary>Browsers cannot set headers on a websocket, so the hub's token arrives as a query parameter (ADR 0012).</summary>
    private static Task ReadHubTokenFromQuery(MessageReceivedContext context)
    {
        if (context.Request.Path.StartsWithSegments(CanvasHubMessages.HubPath))
        {
            var token = context.Request.Query["access_token"].ToString();
            if (token.Length > 0)
            {
                context.Token = token;
            }
        }

        return Task.CompletedTask;
    }
}
