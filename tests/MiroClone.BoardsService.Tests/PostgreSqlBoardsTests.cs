using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using MiroClone.TestSupport;
using Xunit;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// The search on PostgreSQL, in this service's own database. Gated: runs only when TEST_POSTGRES holds a server connection
/// string; otherwise skipped. Sqlite folds ASCII only, so the non-ASCII case is the one that tells the two providers apart.
/// </summary>
public sealed class PostgreSqlBoardsTests : IAsyncLifetime
{
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _workspace = Guid.NewGuid();
    private TestAuthority? _authority;
    private WebApplication? _app;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var databaseConnection = await PostgreSqlTestDatabase.PrepareAsync(connectionString, TestContext.Current.CancellationToken, "boards_search_test");
        _authority = await TestAuthority.StartAsync();
        _app = await ServiceHost.StartAsync(
            new Dictionary<string, string?> { ["ConnectionStrings:boards"] = databaseConnection },
            configureBearer: options => options.BackchannelHttpHandler = _authority.Backchannel);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (_authority is not null)
        {
            await _authority.DisposeAsync();
        }
    }

    [Fact]
    public async Task SearchFoldsCaseOnPostgreSqlForNonAsciiTitles()
    {
        if (_client is null)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL search test is skipped.");
        }

        var created = await SendAsync(HttpMethod.Post, "/api/boards", new { title = "Żółw Roadmap" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var found = await SendAsync(HttpMethod.Get, "/api/boards?search=ŻÓŁW%20roadmap");

        using var document = JsonDocument.Parse(await found.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("total").GetInt32());
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", BoardTokens.Issue(_authority!, _user, _workspace, "Ada")) },
        };
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client!.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
