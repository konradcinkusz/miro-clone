using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.BoardsService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Token validation against authservice (WP03), the "boards" database and its migrations.
    /// Endpoints arrive with WP04; the session stub in <see cref="SessionEndpoints"/> proves the token path.
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
        return services;
    }
}
