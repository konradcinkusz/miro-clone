using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using MiroClone.TestSupport;
using Xunit;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// The WP03 acceptance stub: a token from authservice's shape reaches a protected endpoint on boards-service,
/// and no token does not. Keys come from the JWKS of a local authority, so the test is offline.
/// </summary>
public sealed class SessionEndpointTests : IAsyncLifetime
{
    private const string Audience = "miro-clone";
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _organizationId = Guid.NewGuid();
    private TestAuthority _authority = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _authority = await TestAuthority.StartAsync();
        _app = await ServiceHost.StartAsync(
            // In-memory: the boards migration is PostgreSQL-generated and does not run on SQLite (ADR 0014).
            new Dictionary<string, string?> { ["Database:Provider"] = "InMemory" },
            options => options.BackchannelHttpHandler = _authority.Backchannel);
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
    public async Task RequestWithoutATokenIsRefused()
    {
        var response = await _client.GetAsync("/api/session", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidTokenAnswersWithTheUserAndTheOrganization()
    {
        var token = _authority.IssueToken(Audience, _userId, [_organizationId], DateTime.UtcNow.AddMinutes(5));

        var response = await SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SessionBody>(TestContext.Current.CancellationToken);
        Assert.Equal(_userId, body!.UserId);
        Assert.Equal(_organizationId, body.WorkspaceId);
    }

    [Fact]
    public async Task TokenMintedForAnotherProductIsRefused()
    {
        var token = _authority.IssueToken("another-product", _userId, [_organizationId], DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(token)).StatusCode);
    }

    [Fact]
    public async Task TokenSignedOutsideTheJwksIsRefused()
    {
        using var rogueKey = RSA.Create(2048);
        var token = TokenFactory.Rs256(rogueKey, TestAuthority.KeyId, TestAuthority.Issuer, Audience,
            _userId, [_organizationId], DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(token)).StatusCode);
    }

    [Fact]
    public async Task ProbesStayAnonymous()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void StartupFailsWithoutAnAuthority()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Audience"] = Audience });

        var error = Assert.Throws<InvalidOperationException>(() => builder.Services.AddBoardsService(builder.Configuration));

        Assert.Contains("Auth:Authority", error.Message);
    }

    private Task<HttpResponseMessage> SendAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record SessionBody(Guid UserId, Guid WorkspaceId);
}
