using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using Xunit;

namespace MiroClone.CanvasService.Tests.Api;

/// <summary>The REST surface: membership decides the answer, a non-member gets 404, and the purge is guarded the same way.</summary>
public sealed class CanvasEndpointTests : IAsyncLifetime
{
    private readonly Guid _member = Guid.NewGuid();
    private readonly Guid _board = Guid.NewGuid();
    private readonly Guid _otherBoard = Guid.NewGuid();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await ServiceHost.StartAsync(new Dictionary<string, string?>());
        _app.Services.GetRequiredService<FakeBoardAccess>().Grant(_member, _board);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task AMemberReadsAnEmptyBoardAsCamelCaseState()
    {
        using var request = Get(_board, _member);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"layerIds\":[]", json);
        Assert.Contains("\"version\":0", json);
    }

    [Fact]
    public async Task ANonMemberIsAnsweredNotFoundNeverForbidden()
    {
        using var request = Get(_otherBoard, _member);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ARequestWithoutATokenIsUnauthorized()
    {
        var response = await _client.GetAsync($"/api/canvas/{_board:D}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenForAnotherServiceIsRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/canvas/{_board:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(_member, audience: "another-product"));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BoardsServiceBeingDownIsServiceUnavailableNotAnEmptyBoard()
    {
        _app.Services.GetRequiredService<FakeBoardAccess>().Unavailable = true;
        using var request = Get(_board, _member);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AStoreThatCannotBeReadIsServiceUnavailableForAnUncachedBoard()
    {
        _app.Services.GetRequiredService<FakeSnapshotStore>().Offline = true;
        using var request = Get(_board, _member);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AMemberCannotPurgeTheCanvasOfABoardThatStillExists()
    {
        // The purge is for a deleted board (ADR 0043). BoardsService still shows this board, so the canvas must stay.
        var registry = _app.Services.GetRequiredService<CanvasRegistry>();
        var canvas = await registry.GetOrLoadAsync(_board, TestTokens.Workspace, TestContext.Current.CancellationToken);
        canvas.Apply(new LayerMutation(MutationKind.Upsert, new Layer(Guid.NewGuid(), LayerType.Ellipse, 0, 0, 1, 1, new Rgb(0, 0, 0))));
        registry.MarkDirty(_board);
        await registry.FlushAsync(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/canvas/{_board:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(_member));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(registry.TryGet(_board));
        Assert.NotNull(_app.Services.GetRequiredService<FakeSnapshotStore>().Row(_board));
    }

    [Fact]
    public async Task AnotherWorkspaceCannotPurgeTheCanvas()
    {
        var registry = _app.Services.GetRequiredService<CanvasRegistry>();
        await registry.GetOrLoadAsync(_board, TestTokens.Workspace, TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/canvas/{_board:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(Guid.NewGuid(), Guid.NewGuid()));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(registry.TryGet(_board));
    }

    [Fact]
    public async Task TheOwningWorkspacePurgesADeletedBoard()
    {
        // BoardsService no longer shows the board to anyone, which is what a deleted board looks like from here.
        var deleted = Guid.NewGuid();
        var registry = _app.Services.GetRequiredService<CanvasRegistry>();
        var canvas = await registry.GetOrLoadAsync(deleted, TestTokens.Workspace, TestContext.Current.CancellationToken);
        canvas.Apply(new LayerMutation(MutationKind.Upsert, new Layer(Guid.NewGuid(), LayerType.Ellipse, 0, 0, 1, 1, new Rgb(0, 0, 0))));
        registry.MarkDirty(deleted);
        await registry.FlushAsync(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/canvas/{deleted:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(_member));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(registry.TryGet(deleted));
        Assert.Null(_app.Services.GetRequiredService<FakeSnapshotStore>().Row(deleted));
    }

    [Fact]
    public async Task APurgeFailsClosedWhenBoardsServiceCannotSayWhetherTheBoardIsDeleted()
    {
        var registry = _app.Services.GetRequiredService<CanvasRegistry>();
        await registry.GetOrLoadAsync(_board, TestTokens.Workspace, TestContext.Current.CancellationToken);
        registry.MarkDirty(_board);
        await registry.FlushAsync(TestContext.Current.CancellationToken);
        _app.Services.GetRequiredService<FakeBoardAccess>().Unavailable = true;
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/canvas/{_board:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(_member));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(_app.Services.GetRequiredService<FakeSnapshotStore>().Row(_board));
    }

    [Fact]
    public async Task PurgingABoardWithNoStoredSnapshotIsNoContent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/canvas/{_otherBoard:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(_member));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ATokenWithTwoOrganizationsHasNoActiveWorkspaceAndIsRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/canvas/{_board:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokens.IssueForOrganizations(_member, TestTokens.Workspace, Guid.NewGuid()));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpRequestMessage Get(Guid boardId, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/canvas/{boardId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(userId));
        return request;
    }
}
