using System.Net;
using MiroClone.Web.Auth;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests.Auth;

public sealed class SessionServiceTests : IDisposable
{
    private readonly AuthHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ASessionKeepsItsClaimsAndIsFoundByItsId()
    {
        var sessionId = await _harness.StoreSessionAsync(TestTokens.Issue(organizations: ["org-1"]));

        var principal = await _harness.Sessions.GetPrincipalAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.True(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
        Assert.Equal("org-1", principal!.FindFirst("organization")!.Value);
    }

    [Fact]
    public async Task AFreshTokenIsReturnedWithoutAnyRefreshCall()
    {
        var token = TestTokens.Issue();
        var sessionId = await _harness.StoreSessionAsync(token, expiresIn: 900);

        var returned = await _harness.Sessions.GetAccessTokenAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Equal(token, returned);
        Assert.Empty(_harness.Authority.Calls);
    }

    [Fact]
    public async Task ATokenNearExpiryIsRefreshedOnceForConcurrentCallersAndTheRotationIsKept()
    {
        var sessionId = await _harness.StoreSessionAsync(TestTokens.Issue(), refreshToken: "refresh-1", expiresIn: 30);
        var renewed = TestTokens.Issue(organizations: ["org-1"]);
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/refresh", HttpStatusCode.OK, TestTokens.TokenReply(renewed, "refresh-2", 900));

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _harness.Sessions.GetAccessTokenAsync(sessionId)));

        // authservice rotates refresh tokens: a second use of "refresh-1" would revoke the family, so one refresh is the rule.
        Assert.Equal(1, _harness.Authority.CountCalls("POST", "api/v1/auth/refresh"));
        Assert.All(results, token => Assert.Equal(renewed, token));
        using var sent = System.Text.Json.JsonDocument.Parse(_harness.Authority.Calls.Single().Body!);
        Assert.Equal("refresh-1", sent.RootElement.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task ARefusedRefreshEndsTheSession()
    {
        var sessionId = await _harness.StoreSessionAsync(TestTokens.Issue(), expiresIn: 30);
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/refresh", HttpStatusCode.Unauthorized, new { error = "Invalid or expired refresh token" });

        var token = await _harness.Sessions.GetAccessTokenAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Null(token);
        Assert.False(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnOutageDuringRefreshKeepsTheSession()
    {
        var sessionId = await _harness.StoreSessionAsync(TestTokens.Issue(), expiresIn: 30);
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/refresh", HttpStatusCode.ServiceUnavailable, new { error = "down" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Sessions.GetAccessTokenAsync(sessionId, TestContext.Current.CancellationToken));

        Assert.True(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARefreshedTokenThatFailsValidationEndsTheSession()
    {
        var sessionId = await _harness.StoreSessionAsync(TestTokens.Issue(), expiresIn: 30);
        var wrongAudience = TestTokens.Issue(audience: "some-other-product");
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/refresh", HttpStatusCode.OK, TestTokens.TokenReply(wrongAudience));

        var token = await _harness.Sessions.GetAccessTokenAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Null(token);
        Assert.False(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASessionPastItsAbsoluteEndIsGone()
    {
        var sessionId = await _harness.StoreSessionAsync();

        _harness.Clock.Now = _harness.Clock.Now.AddHours(9);

        Assert.False(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
        Assert.Null(await _harness.Sessions.GetAccessTokenAsync(sessionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveEndsTheSessionAndCanBeRepeated()
    {
        var sessionId = await _harness.StoreSessionAsync();

        await _harness.Sessions.RemoveAsync(sessionId, TestContext.Current.CancellationToken);
        await _harness.Sessions.RemoveAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.False(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BearerHandlerAddsTheSessionsTokenToACall()
    {
        var token = TestTokens.Issue();
        var sessionId = await _harness.StoreSessionAsync(token);
        _harness.Boards.On(HttpMethod.Get, "api/boards", HttpStatusCode.OK, new { items = Array.Empty<object>(), total = 0 });

        await _harness.BoardsClient.ListAsync(sessionId, null, false, 1, 20, TestContext.Current.CancellationToken);

        Assert.Equal($"Bearer {token}", _harness.Boards.Calls.Single().Authorization);
    }

    [Fact]
    public async Task BearerHandlerAnswersUnauthorizedForAnEndedSessionWithoutCallingTheService()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => _harness.BoardsClient.ListAsync("ended-session", null, false, 1, 20, TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Boards.Calls);
    }

    [Fact]
    public async Task ACallWithNoSessionCarriesNoBearer()
    {
        using var invoker = new HttpMessageInvoker(new BearerTokenHandler(_harness.Sessions) { InnerHandler = _harness.Boards });
        _harness.Boards.On(HttpMethod.Get, "api/session", HttpStatusCode.OK);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://boards-service/api/session"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(_harness.Boards.Calls.Single().Authorization);
    }
}
