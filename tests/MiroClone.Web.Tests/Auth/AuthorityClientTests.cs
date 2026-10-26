using System.Net;
using System.Text.Json;
using MiroClone.Web.Auth;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests.Auth;

public sealed class AuthorityClientTests : IDisposable
{
    private readonly AuthHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static readonly ConsentVersions Versions = new("2026-01-01", "2026-01-01", "2026-01-01");

    private static readonly string[] PasswordErrors = ["Password is too common."];

    [Fact]
    public async Task LoginReturnsTheTokensAuthserviceIssued()
    {
        var token = TestTokens.Issue(organizations: ["org-1"]);
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, TestTokens.TokenReply(token, "refresh-9", 900));

        var result = await _harness.AuthorityClient.LoginAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(token, result.Value!.AccessToken);
        Assert.Equal("refresh-9", result.Value.RefreshToken);
        Assert.Equal(900, result.Value.ExpiresIn);
    }

    [Fact]
    public async Task LoginSendsCamelCaseCredentialsAndNoBearer()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.Unauthorized, new { error = "Invalid email or password" });

        await _harness.AuthorityClient.LoginAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        var call = Assert.Single(_harness.Authority.Calls);
        Assert.Null(call.Authorization);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal("owner@dev.test", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("pw-12345678", body.RootElement.GetProperty("password").GetString());
    }

    [Fact]
    public async Task AnUnreachableAuthserviceIsAFailedAnswerNotAnException()
    {
        // A refused connection used to escape as an exception, and the account form answered with a 500 page (ADR 0022).
        _harness.Authority.Offline = true;

        var result = await _harness.AuthorityClient.LoginAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthorityClient.Unreachable, result.Error);
        Assert.Equal(503, result.Status);
    }

    [Fact]
    public async Task ASignInWhileAuthserviceIsDownEndsWithAMessageInTheFlow()
    {
        _harness.Authority.Offline = true;

        var outcome = await _harness.Flows.SignInAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.Equal(AuthorityClient.Unreachable, outcome.Error);
    }

    [Fact]
    public async Task AuthserviceErrorMessageIsShownAsItIs()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.Unauthorized, new { error = "Invalid email or password" });

        var result = await _harness.AuthorityClient.LoginAsync("owner@dev.test", "wrong", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid email or password", result.Error);
        Assert.Equal(401, result.Status);
    }

    [Fact]
    public async Task ValidationErrorsAreReadFromTheErrorsList()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/register", HttpStatusCode.BadRequest, new { errors = PasswordErrors });

        var result = await _harness.AuthorityClient.RegisterAsync("owner@dev.test", "password", Versions, TestContext.Current.CancellationToken);

        Assert.Equal("Password is too common.", result.Error);
    }

    [Fact]
    public async Task ProblemDetailsValidationErrorsAreReadFromTheFieldMap()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/reset-password", HttpStatusCode.BadRequest, new
        {
            title = "One or more validation errors occurred.",
            status = 400,
            errors = new Dictionary<string, string[]> { ["Token"] = ["Reset token is required"] },
        });

        var result = await _harness.AuthorityClient.ResetPasswordAsync("owner@dev.test", string.Empty, "new-pass-123", TestContext.Current.CancellationToken);

        Assert.Equal("Reset token is required", result.Error);
    }

    [Fact]
    public async Task TwoFactorAccountsAreRefusedNotSignedIn()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, new { requiresTwoFactor = true, challengeToken = "c", expiresIn = 300 });

        var result = await _harness.AuthorityClient.LoginAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("second factor", result.Error);
    }

    [Fact]
    public async Task RegistrationSendsTheConsentVersionsTheServiceRequires()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/register", HttpStatusCode.Created, new { userId = "u", email = "a@b.c", message = "check", emailVerificationRequired = true });

        var result = await _harness.AuthorityClient.RegisterAsync("owner@dev.test", "pw-12345678", new ConsentVersions("T1", "P1", "C1"), TestContext.Current.CancellationToken);

        Assert.True(result.Value!.VerificationRequired);
        Assert.Null(result.Value.Tokens);
        using var body = JsonDocument.Parse(_harness.Authority.Calls.Single().Body!);
        Assert.Equal("T1", body.RootElement.GetProperty("acceptedTermsVersion").GetString());
        Assert.Equal("P1", body.RootElement.GetProperty("acceptedPrivacyVersion").GetString());
    }

    [Fact]
    public async Task RegistrationWithoutVerificationCompletesWithTokens()
    {
        var token = TestTokens.Issue();
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/register", HttpStatusCode.OK, TestTokens.TokenReply(token));

        var result = await _harness.AuthorityClient.RegisterAsync("owner@dev.test", "pw-12345678", Versions, TestContext.Current.CancellationToken);

        Assert.False(result.Value!.VerificationRequired);
        Assert.Equal(token, result.Value.Tokens!.AccessToken);
    }

    [Fact]
    public async Task ConsentVersionsAreReadWithNoSession()
    {
        _harness.Authority.On(HttpMethod.Get, "api/v1/auth/consents/versions", HttpStatusCode.OK, new { terms = "T", privacy = "P", cookies = "C" });

        var result = await _harness.AuthorityClient.GetConsentVersionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ConsentVersions("T", "P", "C"), result.Value);
        Assert.Null(_harness.Authority.Calls.Single().Authorization);
    }

    [Fact]
    public async Task OrganizationsListIsReadAsTheSessionAndRejectsAnUnreadableEntry()
    {
        var token = TestTokens.Issue(organizations: ["org-1"]);
        var sessionId = await _harness.StoreSessionAsync(token);
        _harness.Authority.On(HttpMethod.Get, "api/v1/organizations", HttpStatusCode.OK, new[]
        {
            new { id = "org-1", name = "Studio", userRole = "Owner", imageUrl = (string?)null },
        });

        var listed = await _harness.AuthorityClient.ListOrganizationsAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Equal(new AuthorityOrganization("org-1", "Studio", "Owner"), Assert.Single(listed.Value!));
        Assert.Equal($"Bearer {token}", _harness.Authority.Calls.Single().Authorization);

        _harness.Authority.On(HttpMethod.Get, "api/v1/organizations", HttpStatusCode.OK, new[] { new { id = "org-1" } });
        var unreadable = await _harness.AuthorityClient.ListOrganizationsAsync(sessionId, TestContext.Current.CancellationToken);
        Assert.False(unreadable.Succeeded);
        Assert.Equal(AuthorityClient.UnreadableReply, unreadable.Error);
    }

    [Fact]
    public async Task CallForAnEndedSessionReportsTheSessionEnded()
    {
        // No session is stored, so the bearer handler answers 401 before the service is reached.
        var result = await _harness.AuthorityClient.ListOrganizationsAsync("no-such-session", TestContext.Current.CancellationToken);

        Assert.Equal(AccountFlows.SessionEnded, result.Error);
        Assert.Empty(_harness.Authority.Calls);
    }

    [Fact]
    public async Task AnUnparseableBodyIsReportedNotThrown()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/login", HttpStatusCode.OK, "not json at all");

        var result = await _harness.AuthorityClient.LoginAsync("owner@dev.test", "pw-12345678", TestContext.Current.CancellationToken);

        Assert.Equal(AuthorityClient.UnreadableReply, result.Error);
    }

    [Fact]
    public async Task VerifyEmailSucceedsOnAcceptance()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/verify-email", HttpStatusCode.OK, new { message = "Email address verified." });

        var result = await _harness.AuthorityClient.VerifyEmailAsync("owner@dev.test", "tok", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResetPasswordSendsTheNewPassword()
    {
        _harness.Authority.On(HttpMethod.Post, "api/v1/auth/reset-password", HttpStatusCode.OK, new { message = "Password has been reset successfully." });

        var result = await _harness.AuthorityClient.ResetPasswordAsync("owner@dev.test", "tok", "new-pass-123", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        using var body = JsonDocument.Parse(_harness.Authority.Calls.Single().Body!);
        Assert.Equal("new-pass-123", body.RootElement.GetProperty("newPassword").GetString());
    }
}
