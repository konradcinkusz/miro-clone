using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.BoardsService.Boards;
using MiroClone.BoardsService.Canvas;
using MiroClone.BoardsService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService;

public static class ServiceCollectionExtensions
{
    /// <summary>The "boards" database, its migrations, the boards rules and the client that purges canvas snapshots.</summary>
    public static IServiceCollection AddBoardsService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseContext<BoardsDbContext>(configuration, "boards");
        services.AddMigrateOnStartup<BoardsDbContext>();
        services.AddScoped<BoardService>();
        services.AddCanvasPurgeClient(configuration);
        return services;
    }
}
