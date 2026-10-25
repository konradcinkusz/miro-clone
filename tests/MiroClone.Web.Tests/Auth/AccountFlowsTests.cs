using System.Net;
using MiroClone.Web.Auth;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests.Auth;

public sealed class AccountFlowsTests : IDisposable
{
    private readonly AuthHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private void ScriptVersions() =>
        _harness.Authority.On(HttpMethod.Get, "api/v1/auth/consents/versions", HttpStatusCode.OK, new { terms = "T1", privacy = "P1", cookies = "C1" });

    [Fact]
    public async Task ASignInStoresTheSessionAndTheCookieNamesIt()
    {
        var token = TestTokens.Issue(organizations: ["org-1"]);
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, TestTokens.TokenReply(token));
        _harness.Authority.On(HttpMethod.Get, "api/v1/auth/me", HttpStatusCode.OK, new { id = "u", email = "owner@dev.test", requiresConsent = false });

        var outcome = await _harness.Flows.SignInAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        Assert.False(outcome.RequiresConsent);
        Assert.Equal(outcome.SessionId, SessionClaims.SessionId(outcome.CookiePrincipal!));
        Assert.True(await _harness.Sessions.ExistsAsync(outcome.SessionId!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheCookieCarriesIdentityButNoToken()
    {
        var token = TestTokens.Issue(organizations: ["org-1"]);
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, TestTokens.TokenReply(token));
        _harness.Authority.On(HttpMethod.Get, "api/v1/auth/me", HttpStatusCode.OK, new { requiresConsent = false });

        var outcome = await _harness.Flows.SignInAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        var claims = outcome.CookiePrincipal!.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
        Assert.Equal("owner@dev.test", claims["email"]);
        Assert.DoesNotContain(outcome.CookiePrincipal.Claims, claim => claim.Value == token);
        Assert.DoesNotContain(outcome.CookiePrincipal.Claims, claim => claim.Type == "organization");
    }

    [Fact]
    public async Task ASignInWhoseConsentIsOwedStillStartsButSaysSo()
    {
        var token = TestTokens.Issue();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, TestTokens.TokenReply(token));
        _harness.Authority.On(HttpMethod.Get, "api/v1/auth/me", HttpStatusCode.OK, new { requiresConsent = true });

        var outcome = await _harness.Flows.SignInAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.True(outcome.RequiresConsent);
        Assert.True(await _harness.Sessions.ExistsAsync(outcome.SessionId!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASignInFailsClosedWhenConsentCannotBeKnown()
    {
        var token = TestTokens.Issue();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, TestTokens.TokenReply(token));
        _harness.Authority.On(HttpMethod.Get, "api/v1/auth/me", HttpStatusCode.InternalServerError, new { error = "boom" });

        var outcome = await _harness.Flows.SignInAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.Equal("boom", outcome.Error);
        Assert.Null(outcome.CookiePrincipal);
        Assert.Null(outcome.SessionId);
    }

    [Fact]
    public async Task ASignInWithATokenThisEstateDoesNotAcceptIsRefused()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, TestTokens.TokenReply(TestTokens.Issue(audience: "other-product")));

        var outcome = await _harness.Flows.SignInAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.Contains("cannot verify", outcome.Error);
        Assert.Null(outcome.SessionId);
    }

    [Fact]
    public async Task AFailedSignInShowsAuthservicesMessage()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.Forbidden, new { error = "Please verify your email first." });

        var outcome = await _harness.Flows.SignInAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.Equal("Please verify your email first.", outcome.Error);
    }

    [Fact]
    public async Task SignUpRefusesWithoutBothAcceptances()
    {
        var outcome = await _harness.Flows.SignUpAsync("owner@dev.test", "pw-12345678", termsAccepted: true, privacyAccepted: false, TestContext.Current.CancellationToken);

        Assert.Equal(AccountFlows.TermsRequired, outcome.Error);
        Assert.Empty(_harness.Authority.Calls);
    }

    [Fact]
    public async Task SignUpWaitsForVerificationWithoutASession()
    {
        ScriptVersions();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/register", HttpStatusCode.Created, new { userId = "u", emailVerificationRequired = true });

        var outcome = await _harness.Flows.SignUpAsync("owner@dev.test", "pw-12345678", termsAccepted: true, privacyAccepted: true, TestContext.Current.CancellationToken);

        Assert.True(outcome.VerificationRequired);
        Assert.Null(outcome.SessionId);
    }

    [Fact]
    public async Task SignUpAtTheCurrentVersionsCompletesWhenNoVerificationIsNeeded()
    {
        ScriptVersions();
        var token = TestTokens.Issue();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/register", HttpStatusCode.OK, TestTokens.TokenReply(token));
        _harness.Authority.On(HttpMethod.Get, "api/v1/auth/me", HttpStatusCode.OK, new { requiresConsent = false });

        var outcome = await _harness.Flows.SignUpAsync("owner@dev.test", "pw-12345678", termsAccepted: true, privacyAccepted: true, TestContext.Current.CancellationToken);

        Assert.NotNull(outcome.SessionId);
        Assert.False(outcome.VerificationRequired);
    }

    [Fact]
    public async Task SignOutRevokesAtAuthserviceAndRemovesTheSession()
    {
        var sessionId = await _harness.StoreSessionAsync();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/logout", HttpStatusCode.OK, new { message = "Logged out successfully" });

        await _harness.Flows.EndAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.False(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
        Assert.Equal(1, _harness.Authority.CountCalls("POST", "api/v1/auth/logout"));
        Assert.StartsWith("Bearer ", _harness.Authority.Calls.Single().Authorization);
    }

    [Fact]
    public async Task SignOutStillEndsTheLocalSessionWhenAuthserviceIsDown()
    {
        var sessionId = await _harness.StoreSessionAsync();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/logout", HttpStatusCode.ServiceUnavailable, new { error = "down" });

        await _harness.Flows.EndAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.False(await _harness.Sessions.ExistsAsync(sessionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcceptingConsentForAnEndedSessionSaysSo()
    {
        var outcome = await _harness.Flows.AcceptConsentAsync("ended-session", TestContext.Current.CancellationToken);

        Assert.Equal(AccountFlows.SessionEnded, outcome.Error);
        Assert.Empty(_harness.Authority.Calls);
    }

    [Fact]
    public async Task AcceptingConsentSendsTheSessionsTokenAndRecordsBoth()
    {
        var sessionId = await _harness.StoreSessionAsync();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/consents", HttpStatusCode.OK, new { message = "ok" });

        var outcome = await _harness.Flows.AcceptConsentAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        var call = _harness.Authority.Calls.Single();
        Assert.StartsWith("Bearer ", call.Authorization);
        Assert.Contains("\"acceptedTerms\":true", call.Body);
        Assert.Contains("\"acceptedPrivacy\":true", call.Body);
    }
}
