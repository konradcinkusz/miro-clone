using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace MiroClone.Web.Auth;

/// <summary>
/// The sign-in, sign-up, sign-out and consent steps, without HTTP. The account endpoints turn each result into a cookie and a
/// redirect; the rules live here so they are tested without a host.
/// </summary>
public sealed class AccountFlows(AuthorityClient authority, AccessTokenValidator validator, SessionService sessions)
{
    public const string SessionEnded = "Your session ended. Sign in again.";
    public const string TermsRequired = "Accept the terms and the privacy policy to create an account.";
    private const string TokenUnreadable = "The sign-in service issued a token this app cannot verify.";

    /// <summary>Signs in with email and password and starts a server-side session.</summary>
    public async Task<AccountOutcome> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var login = await authority.LoginAsync(email, password, ct);
        return login.Succeeded ? await StartSessionAsync(login.Value!, ct) : AccountOutcome.Failed(login.Error!);
    }

    /// <summary>
    /// Creates an account at the consent versions authservice currently requires. An account that must verify its address
    /// gets no session yet: the outcome says so and the caller sends the user to the verification page.
    /// </summary>
    public async Task<AccountOutcome> SignUpAsync(string email, string password, bool termsAccepted, bool privacyAccepted, CancellationToken ct = default)
    {
        if (!termsAccepted || !privacyAccepted)
        {
            return AccountOutcome.Failed(TermsRequired);
        }

        var versions = await authority.GetConsentVersionsAsync(ct);
        if (!versions.Succeeded)
        {
            return AccountOutcome.Failed(versions.Error!);
        }

        var registered = await authority.RegisterAsync(email, password, versions.Value!, ct);
        if (!registered.Succeeded)
        {
            return AccountOutcome.Failed(registered.Error!);
        }

        return registered.Value!.VerificationRequired
            ? AccountOutcome.VerificationPending
            : await StartSessionAsync(registered.Value.Tokens!, ct);
    }

    /// <summary>Records consent for the signed-in session. The session is the only proof of who is asking.</summary>
    public async Task<AccountOutcome> AcceptConsentAsync(string sessionId, CancellationToken ct = default)
    {
        var accepted = await authority.AcceptConsentAsync(sessionId, ct);
        if (!accepted.Succeeded)
        {
            return AccountOutcome.Failed(accepted.Error!);
        }

        return AccountOutcome.Accepted;
    }

    /// <summary>
    /// Ends the session: authservice revokes the user's refresh tokens (its logout), and the stored session is removed.
    /// The network call is best effort; the local session ends either way.
    /// </summary>
    public async Task EndAsync(string? sessionId, CancellationToken ct = default)
    {
        if (sessionId is null)
        {
            return;
        }

        try
        {
            await authority.LogoutAsync(sessionId, ct);
        }
        catch (HttpRequestException)
        {
            // The local session still ends below; authservice's tokens expire on their own schedule.
        }

        await sessions.RemoveAsync(sessionId, ct);
    }

    private async Task<AccountOutcome> StartSessionAsync(AuthorityTokens tokens, CancellationToken ct)
    {
        ClaimsPrincipal principal;
        try
        {
            principal = await validator.ValidateAsync(tokens.AccessToken, ct);
        }
        catch (SecurityTokenException)
        {
            return AccountOutcome.Failed(TokenUnreadable);
        }

        var sessionId = await sessions.CreateAsync(tokens, principal, ct);

        // Fail closed: without knowing whether consent is owed, no session is kept.
        var consent = await authority.RequiresConsentAsync(sessionId, ct);
        if (!consent.Succeeded)
        {
            await sessions.RemoveAsync(sessionId, ct);
            return AccountOutcome.Failed(consent.Error!);
        }

        return AccountOutcome.SignedIn(sessionId, SessionClaims.ForCookie(principal, sessionId), consent.Value);
    }
}

/// <summary>The result of an account step. Exactly one of the shapes below is meaningful for each step.</summary>
public sealed record AccountOutcome
{
    public string? Error { get; private init; }

    public bool VerificationRequired { get; private init; }

    public string? SessionId { get; private init; }

    public ClaimsPrincipal? CookiePrincipal { get; private init; }

    public bool RequiresConsent { get; private init; }

    public static AccountOutcome Failed(string message) => new() { Error = message };

    public static AccountOutcome VerificationPending => new() { VerificationRequired = true };

    public static AccountOutcome Accepted => new();

    public static AccountOutcome SignedIn(string sessionId, ClaimsPrincipal cookie, bool requiresConsent) =>
        new() { SessionId = sessionId, CookiePrincipal = cookie, RequiresConsent = requiresConsent };
}
