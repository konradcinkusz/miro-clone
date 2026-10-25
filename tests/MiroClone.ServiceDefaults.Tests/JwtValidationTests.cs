using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>JWT validation against a JWKS served by a local authority in the test host (no network).</summary>
public sealed class JwtValidationTests : IAsyncLifetime
{
    private const string Audience = "miro-clone-boards";
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private TestAuthority _authority = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _authority = await TestAuthority.StartAsync();

        var builder = TestApps.NewBuilder(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = TestAuthority.Issuer,
            ["Auth:Audience"] = Audience,
        });
        builder.Services.AddJwtValidation(
            builder.Configuration,
            options => options.BackchannelHttpHandler = _authority.Backchannel);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/whoami", (ClaimsPrincipal user) => Results.Json(new
        {
            userId = WorkspaceClaims.UserId(user),
            workspaceId = WorkspaceClaims.WorkspaceId(user),
        })).RequireAuthorization();

        await _app.StartAsync();
        _client = _app.GetTestServer().CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _authority.DisposeAsync();
    }

    [Fact]
    public async Task ValidTokenIsAcceptedAndItsUserAndWorkspaceAreRead()
    {
        var token = _authority.IssueToken(Audience, _userId, _workspaceId, DateTime.UtcNow.AddMinutes(5));

        var response = await SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(_userId.ToString(), body);
        Assert.Contains(_workspaceId.ToString(), body);
    }

    [Fact]
    public async Task MissingTokenIsRefused()
    {
        var response = await _client.GetAsync("/whoami", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenForAnotherAudienceIsRefused()
    {
        var token = _authority.IssueToken("another-service", _userId, _workspaceId, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(token)).StatusCode);
    }

    [Fact]
    public async Task TokenExpiredBeyondClockSkewIsRefused()
    {
        var token = _authority.IssueToken(Audience, _userId, _workspaceId, DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(token)).StatusCode);
    }

    [Fact]
    public async Task TokenSignedByAKeyOutsideTheJwksIsRefused()
    {
        using var rogueKey = System.Security.Cryptography.RSA.Create(2048);
        var token = TokenFactory.Rs256(rogueKey, TestAuthority.KeyId, TestAuthority.Issuer, Audience,
            _userId, _workspaceId, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(token)).StatusCode);
    }

    [Fact]
    public async Task SymmetricHS256TokenIsRefusedEvenWithAValidAudience()
    {
        var token = TokenFactory.Hs256(Audience, _userId, _workspaceId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(token)).StatusCode);
    }

    [Fact]
    public void StartupFailsOnAnEmptyAuthority()
    {
        var builder = TestApps.NewBuilder(new Dictionary<string, string?>
        {
            ["Auth:Audience"] = Audience,
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            builder.Services.AddJwtValidation(builder.Configuration));

        Assert.Contains("Auth:Authority", error.Message);
    }

    private Task<HttpResponseMessage> SendAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
