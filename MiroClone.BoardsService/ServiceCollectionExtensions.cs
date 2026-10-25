using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.BoardsService.Boards;
using MiroClone.BoardsService.Canvas;
using MiroClone.BoardsService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Token validation against authservice (WP03), the "boards" database and its migrations, the boards rules, and the
    /// client that purges canvas snapshots.
    /// </summary>
    /// <param name="configureBearer">Test hook for the JWKS backchannel. Production leaves it unset.</param>
    public static IServiceCollection AddBoardsService(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configureBearer = null)
    {
        services.AddJwtValidation(configuration, configureBearer);
        services.AddDatabaseContext<BoardsDbContext>(configuration, "boards");
        services.AddMigrateOnStartup<BoardsDbContext>();
        services.AddScoped<BoardService>();
        services.AddCanvasPurgeClient(configuration);
        return services;
    }
}
