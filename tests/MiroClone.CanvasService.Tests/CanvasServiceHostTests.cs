using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Persistence;
using Xunit;

namespace MiroClone.CanvasService.Tests;

/// <summary>The service starts, migrates its own database and answers the probe; its guard refuses a foreign origin.</summary>
public sealed class CanvasServiceHostTests : IAsyncLifetime
{
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"canvas-{Guid.NewGuid():N}.db");
    private WebApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await ServiceHost.StartAsync(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:canvas"] = $"Data Source={_databaseFile}",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databaseFile);
    }

    [Fact]
    public async Task StartupAppliesEveryMigrationBeforeTheProbeAnswers()
    {
        using var scope = _app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<CanvasDbContext>().Database;

        var applied = await database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(database.GetMigrations(), applied);
        Assert.NotEmpty(applied);
    }

    [Fact]
    public async Task HealthProbeAnswersOk()
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ForeignOriginIsRefusedByTheOriginGuard()
    {
        using var client = _app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }
}
