using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.CanvasService;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Token validation against authservice (WP03), the "canvas" database and its migrations.
    /// The canvas aggregate and hub arrive with WP05, which protects them with the validation wired here.
    /// </summary>
    /// <param name="configureBearer">Test hook for the JWKS backchannel. Production leaves it unset.</param>
    public static IServiceCollection AddCanvasService(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configureBearer = null)
    {
        services.AddJwtValidation(configuration, configureBearer);
        services.AddDatabaseContext<CanvasDbContext>(configuration, "canvas");
        services.AddMigrateOnStartup<CanvasDbContext>();
        return services;
    }
}
