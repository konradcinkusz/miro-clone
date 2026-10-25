using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.BoardsService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService;

public static class ServiceCollectionExtensions
{
    /// <summary>The "boards" database and its migrations. Endpoints arrive with WP04.</summary>
    public static IServiceCollection AddBoardsService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseContext<BoardsDbContext>(configuration, "boards");
        services.AddMigrateOnStartup<BoardsDbContext>();
        return services;
    }
}
