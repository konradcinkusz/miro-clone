using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.BoardsService.Canvas;
using MiroClone.BoardsService.Persistence;
using Xunit;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// The /api/boards contract end to end: real JWT validation against a local JWKS, the purge client against a fake
/// CanvasService, and every id route checked for the cross-workspace 404. The database is the in-memory provider with a
/// name unique to this test instance: migrations are PostgreSQL migrations and do not run on another provider, so their
/// schema is verified in the PostgreSQL-gated tests instead.
/// </summary>
public sealed class BoardsApiTests : IAsyncLifetime
{
    private readonly string _databaseName = $"boards-api-{Guid.NewGuid():N}";
    private readonly Guid _ada = Guid.NewGuid();
    private readonly Guid _grace = Guid.NewGuid();
    private readonly Guid _workspace = Guid.NewGuid();
    private readonly Guid _otherWorkspace = Guid.NewGuid();
    private readonly FakeCanvas _canvas = new();
    private TestAuthority _authority = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _authority = await TestAuthority.StartAsync();
        _app = await ServiceHost.StartAsync(
            new Dictionary<string, string?> { ["Database:Provider"] = "InMemory" },
            authorityBackchannel: _authority.Backchannel,
            configureServices: services =>
            {
                services.AddDbContext<BoardsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
                services.AddHttpClient<CanvasPurgeClient>().ConfigurePrimaryHttpMessageHandler(() => _canvas);
            });
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _authority.DisposeAsync();
    }

    [Fact]
    public async Task CreateReturnsTheBoardWithAnImageSeedAndTheAuthorsName()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/boards", _ada, _workspace, new { title = "  Roadmap  " }, name: "Ada Lovelace");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var board = await ReadJsonAsync(response);
        Assert.Equal("Roadmap", board.GetProperty("title").GetString());
        Assert.Equal("Ada Lovelace", board.GetProperty("authorName").GetString());
        Assert.Equal(_ada, board.GetProperty("authorId").GetGuid());
        Assert.InRange(board.GetProperty("imageSeed").GetInt32(), 1, 10);
        Assert.False(board.GetProperty("isFavourite").GetBoolean());
        Assert.Equal(board.GetProperty("id").GetGuid(), BoardIdFromLocation(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateRefusesAMissingOrBlankTitleWithAProblemNamingTheField(string? title)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/boards", _ada, _workspace, new { title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));
    }

    [Fact]
    public async Task TitlesOfExactlySixtyCharactersAreAcceptedAndSixtyOneAreRefused()
    {
        var sixty = new string('a', 60);
        var sixtyOne = new string('a', 61);

        var accepted = await SendAsync(HttpMethod.Post, "/api/boards", _ada, _workspace, new { title = sixty });
        var refused = await SendAsync(HttpMethod.Post, "/api/boards", _ada, _workspace, new { title = sixtyOne });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task ListPaginatesAndClampsPageAndPageSize()
    {
        for (var index = 1; index <= 3; index++)
        {
            await CreateBoardAsync($"Board {index}");
        }

        var secondPage = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?pageSize=2&page=2", _ada, _workspace));
        var zeroes = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?pageSize=0&page=0", _ada, _workspace));

        Assert.Equal(3, secondPage.GetProperty("total").GetInt32());
        Assert.Single(secondPage.GetProperty("items").EnumerateArray());
        Assert.Equal(3, zeroes.GetProperty("total").GetInt32());
        Assert.Single(zeroes.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task SearchIsACaseInsensitiveContainsMatchAndTreatsWildcardsAsText()
    {
        await CreateBoardAsync("Roadmap Q3");
        await CreateBoardAsync("road trip");
        await CreateBoardAsync("Budget");

        var roads = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?search=ROAD", _ada, _workspace));
        var quarter = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?search=q3", _ada, _workspace));
        var percent = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?search=%25", _ada, _workspace));

        Assert.Equal(2, roads.GetProperty("total").GetInt32());
        Assert.Equal(1, quarter.GetProperty("total").GetInt32());
        Assert.Equal(0, percent.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task SearchMatchesNonAsciiTitlesCaseInsensitively()
    {
        await CreateBoardAsync("Żółw");

        var lower = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?search=żółw", _ada, _workspace));

        Assert.Equal(1, lower.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task FavouriteIsIdempotentAndFiltersTheList()
    {
        var boardId = await CreateBoardAsync("Starred");
        await CreateBoardAsync("Plain");

        var first = await SendAsync(HttpMethod.Put, $"/api/boards/{boardId}/favourite", _ada, _workspace);
        var second = await SendAsync(HttpMethod.Put, $"/api/boards/{boardId}/favourite", _ada, _workspace);
        var favourites = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?favourites=true", _ada, _workspace));
        var all = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards", _ada, _workspace));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(1, favourites.GetProperty("total").GetInt32());
        Assert.Equal(boardId, favourites.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.True(favourites.GetProperty("items")[0].GetProperty("isFavourite").GetBoolean());
        Assert.Equal(2, all.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ClearingAFavouriteIsIdempotentAndRemovesItFromTheFilter()
    {
        var boardId = await CreateBoardAsync("Starred");
        await SendAsync(HttpMethod.Put, $"/api/boards/{boardId}/favourite", _ada, _workspace);

        var first = await SendAsync(HttpMethod.Delete, $"/api/boards/{boardId}/favourite", _ada, _workspace);
        var second = await SendAsync(HttpMethod.Delete, $"/api/boards/{boardId}/favourite", _ada, _workspace);
        var favourites = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?favourites=true", _ada, _workspace));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(0, favourites.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task FavouritesArePerUser()
    {
        var boardId = await CreateBoardAsync("Shared");
        await SendAsync(HttpMethod.Put, $"/api/boards/{boardId}/favourite", _ada, _workspace);

        var graceView = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?favourites=true", _grace, _workspace));

        Assert.Equal(0, graceView.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task RenameTrimsAndPersistsAndRefusesAnInvalidTitle()
    {
        var boardId = await CreateBoardAsync("Old name");

        var renamed = await SendAsync(HttpMethod.Patch, $"/api/boards/{boardId}", _ada, _workspace, new { title = " New name " });
        var refused = await SendAsync(HttpMethod.Patch, $"/api/boards/{boardId}", _ada, _workspace, new { title = "" });
        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?search=new", _ada, _workspace));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("New name", (await ReadJsonAsync(renamed)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task DeleteRemovesTheBoardItsFavouritesAndPurgesTheCanvasWithTheCallersToken()
    {
        var boardId = await CreateBoardAsync("Doomed");
        await SendAsync(HttpMethod.Put, $"/api/boards/{boardId}/favourite", _ada, _workspace);
        var token = _authority.IssueToken(_ada, _workspace, "Ada");

        var deleted = await SendAsync(HttpMethod.Delete, $"/api/boards/{boardId}", _ada, _workspace, token: token);
        var again = await SendAsync(HttpMethod.Delete, $"/api/boards/{boardId}", _ada, _workspace);
        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards", _ada, _workspace));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(0, list.GetProperty("total").GetInt32());
        using (var scope = _app.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<BoardsDbContext>();
            Assert.Empty(await database.BoardFavourites.ToListAsync(TestContext.Current.CancellationToken));
        }

        var purge = Assert.Single(_canvas.Calls);
        Assert.Equal(HttpMethod.Delete, purge.Method);
        Assert.Equal($"/api/canvas/{boardId:D}", purge.Path);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", token), purge.Authorization);
    }

    [Fact]
    public async Task ACanvasPurgeThatFailsDoesNotUndoTheCommittedDelete()
    {
        var boardId = await CreateBoardAsync("Orphaned snapshot");
        _canvas.Status = HttpStatusCode.InternalServerError;

        var deleted = await SendAsync(HttpMethod.Delete, $"/api/boards/{boardId}", _ada, _workspace);
        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards", _ada, _workspace));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, list.GetProperty("total").GetInt32());
        Assert.NotEmpty(_canvas.Calls);
    }

    [Fact]
    public async Task AnUnreachableCanvasDoesNotUndoTheCommittedDelete()
    {
        var boardId = await CreateBoardAsync("Unreachable canvas");
        _canvas.Failure = new HttpRequestException("canvas is down");

        var deleted = await SendAsync(HttpMethod.Delete, $"/api/boards/{boardId}", _ada, _workspace);
        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards", _ada, _workspace));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AccessAnswersNoContentForABoardInTheWorkspaceAndNotFoundOtherwise()
    {
        var boardId = await CreateBoardAsync("Mine");
        var foreign = await CreateBoardAsync("Theirs", _grace, _otherWorkspace);

        var own = await SendAsync(HttpMethod.Get, $"/api/boards/{boardId}/access", _ada, _workspace);
        var other = await SendAsync(HttpMethod.Get, $"/api/boards/{foreign}/access", _ada, _workspace);
        var missing = await SendAsync(HttpMethod.Get, $"/api/boards/{Guid.NewGuid()}/access", _ada, _workspace);

        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ListOnlyShowsTheCallersWorkspace()
    {
        await CreateBoardAsync("Ours");
        await CreateBoardAsync("Theirs", _grace, _otherWorkspace);

        var mine = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards", _ada, _workspace));
        var search = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards?search=theirs", _ada, _workspace));

        Assert.Equal(1, mine.GetProperty("total").GetInt32());
        Assert.Equal(0, search.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// Every id route, called from another workspace, answers 404 and changes nothing. The board is re-read from its own
    /// workspace afterwards, so a route that wrote before refusing would show here.
    /// </summary>
    [Theory]
    [InlineData("PATCH", "rename")]
    [InlineData("DELETE", "delete")]
    [InlineData("PUT", "favourite")]
    [InlineData("DELETE", "unfavourite")]
    [InlineData("GET", "access")]
    public async Task EveryIdRouteAnswersNotFoundForAnotherWorkspacesBoard(string method, string route)
    {
        var boardId = await CreateBoardAsync("Not yours to touch", _grace, _otherWorkspace);
        var url = route switch
        {
            "rename" or "delete" => $"/api/boards/{boardId}",
            "favourite" => $"/api/boards/{boardId}/favourite",
            "unfavourite" => $"/api/boards/{boardId}/favourite",
            _ => $"/api/boards/{boardId}/access",
        };
        object? body = route == "rename" ? new { title = "Hijacked" } : null;

        var response = await SendAsync(new HttpMethod(method), url, _ada, _workspace, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var owner = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/boards", _grace, _otherWorkspace));
        var board = Assert.Single(owner.GetProperty("items").EnumerateArray());
        Assert.Equal("Not yours to touch", board.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ARequestWithoutATokenIsRefused()
    {
        var response = await _client.GetAsync("/api/boards", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AValidTokenWithNoActiveWorkspaceIsForbiddenNotDefaulted()
    {
        var token = _authority.IssueToken(_ada, workspaceId: null);

        var response = await SendAsync(HttpMethod.Get, "/api/boards", _ada, _workspace, token: token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreateBoardAsync(string title, Guid? user = null, Guid? workspace = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/boards", user ?? _ada, workspace ?? _workspace, new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return BoardIdFromLocation(response);
    }

    private static Guid BoardIdFromLocation(HttpResponseMessage response) =>
        Guid.Parse(response.Headers.Location!.ToString().Split('/').Last());

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        Guid user,
        Guid workspace,
        object? body = null,
        string? name = null,
        string? token = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token ?? _authority.IssueToken(user, workspace, name ?? "Member")) },
        };
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }
}
