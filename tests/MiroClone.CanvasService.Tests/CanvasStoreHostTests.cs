using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using Xunit;

namespace MiroClone.CanvasService.Tests;

/// <summary>
/// The accept criterion: a restart keeps layers, and a dead store degrades the health probe without failing it (P8).
/// </summary>
public sealed class CanvasStoreHostTests : IAsyncLifetime
{
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"canvas-restart-{Guid.NewGuid():N}.db");
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _board = Guid.NewGuid();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databaseFile);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ARestartKeepsTheLayersTheBoardHeld()
    {
        var layer = new Layer(Guid.NewGuid(), LayerType.Note, 12, 34, 200, 120, new Rgb(253, 224, 71), Value: "kept");
        await using (var first = await StartAsync(ServiceHost.SqliteFile(_databaseFile), realStore: true))
        {
            var registry = first.Services.GetRequiredService<CanvasRegistry>();
            var canvas = await registry.GetOrLoadAsync(_board, TestContext.Current.CancellationToken);
            canvas.Apply(new LayerMutation(MutationKind.Upsert, layer));
            registry.MarkDirty(_board);

            // Stopping the host runs the shutdown flush, which is the path a clean restart takes.
            await first.StopAsync(TestContext.Current.CancellationToken);
        }

        await using var second = await StartAsync(ServiceHost.SqliteFile(_databaseFile), realStore: true);
        using var client = second.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(_userId));

        var response = await client.GetAsync($"/api/canvas/{_board:D}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = JsonSerializer.Deserialize<CanvasState>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            CanvasJson.Options)!;
        Assert.Equal(1, state.Version);
        Assert.Equal(layer.Id, Assert.Single(state.LayerIds));
        Assert.Equal("kept", state.Layers[layer.Id].Value);
    }

    [Fact]
    public async Task AStoreThatIsDownDegradesTheProbeButTheProbeStillAnswersOk()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>());
        var registry = app.Services.GetRequiredService<CanvasRegistry>();
        var canvas = await registry.GetOrLoadAsync(_board, TestContext.Current.CancellationToken);
        canvas.Apply(new LayerMutation(MutationKind.Upsert, new Layer(Guid.NewGuid(), LayerType.Rectangle, 0, 0, 1, 1, new Rgb(0, 0, 0))));
        registry.MarkDirty(_board);
        app.Services.GetRequiredService<FakeSnapshotStore>().Offline = true;
        await registry.FlushAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Degraded", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Starts a host with the test board granted to the caller, so each test states only what it exercises.</summary>
    private async Task<WebApplication> StartAsync(IDictionary<string, string?> settings, bool realStore = false)
    {
        var app = await ServiceHost.StartAsync(settings, realStore);
        app.Services.GetRequiredService<FakeBoardAccess>().Grant(_userId, _board);
        return app;
    }
}
