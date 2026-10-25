using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Persistence;
using MiroClone.ServiceDefaults;
using Xunit;

namespace MiroClone.CanvasService.Tests;

/// <summary>The same migrations on PostgreSQL, in this service's own database. Runs only when TEST_POSTGRES holds a server connection string; otherwise skipped.</summary>
public sealed class PostgreSqlMigrationTests
{
    [Fact]
    public async Task MigrationsApplyToPostgreSql()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL migration test is skipped.");
        }

        var databaseConnection = await PostgreSqlTestDatabase.PrepareAsync(connectionString, TestContext.Current.CancellationToken);
        await using var app = await ServiceHost.StartAsync(new Dictionary<string, string?>
        {
            ["Database:Provider"] = DatabaseProviders.Npgsql,
            ["ConnectionStrings:canvas"] = databaseConnection,
        });
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<CanvasDbContext>().Database;

        var applied = await database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(database.GetMigrations(), applied);
    }
}
