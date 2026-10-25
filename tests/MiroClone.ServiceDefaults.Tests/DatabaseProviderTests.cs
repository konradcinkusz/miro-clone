using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>Provider selection. The PostgreSQL case runs only when TEST_POSTGRES names a reachable database.</summary>
public sealed class DatabaseProviderTests
{
    [Fact]
    public async Task SqliteProviderIsSelectedFromConfiguration()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>
        {
            ["Database:Provider"] = DatabaseProviders.Sqlite,
            ["ConnectionStrings:probe"] = "Data Source=:memory:",
        });
        builder.Services.AddDatabaseContext<ProbeContext>(builder.Configuration, "probe");
        await using var app = builder.Build();

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ProbeContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        Assert.True(await context.Database.CanConnectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void InMemoryProviderIsSelectedFromConfiguration()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>
        {
            ["Database:Provider"] = DatabaseProviders.InMemory,
        });

        builder.Services.AddDatabaseContext<ProbeContext>(builder.Configuration, "probe");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Equal(
            "Microsoft.EntityFrameworkCore.InMemory",
            scope.ServiceProvider.GetRequiredService<ProbeContext>().Database.ProviderName);
    }

    [Fact]
    public void MissingConnectionStringFailsAtRegistrationNotSilentlyInMemory()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>());

        var error = Assert.Throws<InvalidOperationException>(() =>
            builder.Services.AddDatabaseContext<ProbeContext>(builder.Configuration, "probe"));

        Assert.Contains("ConnectionStrings:probe", error.Message);
    }

    [Fact]
    public void UnknownProviderIsRefusedAtRegistration()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Oracle",
        });

        Assert.Throws<InvalidOperationException>(() =>
            builder.Services.AddDatabaseContext<ProbeContext>(builder.Configuration, "probe"));
    }

    [Fact]
    public async Task PostgreSqlProviderConnectsWhenTestPostgresIsSet()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("TEST_POSTGRES is not set; no local PostgreSQL to test against.");
        }

        var builder = TestApps.NewBuilder(new Dictionary<string, string?>
        {
            ["ConnectionStrings:probe"] = connectionString,
        });
        builder.Services.AddDatabaseContext<ProbeContext>(builder.Configuration, "probe");
        await using var app = builder.Build();

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ProbeContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.True(await context.Database.CanConnectAsync(TestContext.Current.CancellationToken));
    }
}

/// <summary>A context with no entities: the tests exercise provider wiring, not a schema.</summary>
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options);
