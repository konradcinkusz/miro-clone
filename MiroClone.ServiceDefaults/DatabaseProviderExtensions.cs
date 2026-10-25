using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MiroClone.ServiceDefaults;

/// <summary>Values accepted under Database:Provider. Strings, not an enum: the kernel holds no enums (P2).</summary>
public static class DatabaseProviders
{
    public const string Npgsql = "Npgsql";
    public const string Sqlite = "Sqlite";
    public const string InMemory = "InMemory";
}

public static class DatabaseProviderExtensions
{
    /// <summary>
    /// Registers a DbContext on the provider named by Database:Provider (default Npgsql). Npgsql gets a retry policy.
    /// A missing connection string is a startup error, not a silent fallback to memory: data must not vanish quietly.
    /// </summary>
    /// <param name="connectionName">Name under ConnectionStrings, also used as the in-memory database name.</param>
    public static IServiceCollection AddDatabaseContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionName)
        where TContext : DbContext
    {
        var provider = configuration["Database:Provider"] ?? DatabaseProviders.Npgsql;
        switch (provider)
        {
            case DatabaseProviders.Npgsql:
            {
                var connectionString = RequireConnectionString(configuration, connectionName);
                services.AddDbContext<TContext>(options =>
                    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));
                break;
            }
            case DatabaseProviders.Sqlite:
            {
                var connectionString = RequireConnectionString(configuration, connectionName);
                services.AddDbContext<TContext>(options => options.UseSqlite(connectionString));
                break;
            }
            case DatabaseProviders.InMemory:
                services.AddDbContext<TContext>(options => options.UseInMemoryDatabase(connectionName));
                break;
            default:
                throw new InvalidOperationException($"Unknown database provider '{provider}'.");
        }

        return services;
    }

    private static string RequireConnectionString(IConfiguration configuration, string connectionName)
    {
        var connectionString = configuration.GetConnectionString(connectionName);
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException($"ConnectionStrings:{connectionName} is not configured.")
            : connectionString;
    }
}
