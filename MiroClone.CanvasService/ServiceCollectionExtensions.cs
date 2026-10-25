using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.CanvasService;

public static class ServiceCollectionExtensions
{
    /// <summary>The "canvas" database and its migrations. The canvas aggregate and hub arrive with WP05.</summary>
    public static IServiceCollection AddCanvasService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseContext<CanvasDbContext>(configuration, "canvas");
        services.AddMigrateOnStartup<CanvasDbContext>();
        return services;
    }
}
