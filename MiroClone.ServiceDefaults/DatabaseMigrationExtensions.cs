using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MiroClone.ServiceDefaults;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations before the host starts listening (P4: migrated, never "ensured").
    /// The migration runs in <see cref="IHostedLifecycleService.StartingAsync"/>, which precedes every hosted
    /// service's StartAsync, including the web server's, so no request is served on a stale schema. A non-relational
    /// provider has no schema to migrate and is skipped. A failed migration stops startup.
    /// </summary>
    public static IServiceCollection AddMigrateOnStartup<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddSingleton<IHostedService, DatabaseMigrationHostedService<TContext>>();
        return services;
    }
}

internal sealed class DatabaseMigrationHostedService<TContext>(IServiceScopeFactory scopes) : IHostedLifecycleService
    where TContext : DbContext
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>().Database;
        if (database.IsRelational())
        {
            await database.MigrateAsync(cancellationToken);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
