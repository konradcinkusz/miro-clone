using System.Security.Claims;

namespace MiroClone.Web.Auth;

/// <summary>
/// The cookie's claims. The cookie is a pointer, not a credential: it names the stored session and carries the identity the
/// circuit displays. Workspace membership is read from the stored access token, never from the cookie, so it is never stale
/// after a refresh.
/// </summary>
public static class SessionClaims
{
    public const string AuthenticationType = "MiroClone.Session";

    /// <summary>Claim naming the server-side session the cookie points at.</summary>
    public const string SessionIdClaimType = "miroclone_session";

    public static string? SessionId(ClaimsPrincipal principal) => principal.FindFirst(SessionIdClaimType)?.Value;

    /// <summary>The identity the cookie carries: the subject, the email and whether it is verified, plus the session pointer.</summary>
    public static ClaimsPrincipal ForCookie(ClaimsPrincipal validated, string sessionId)
    {
        var claims = new List<Claim>();
        foreach (var type in new[] { "sub", "email", "email_verified" })
        {
            var claim = validated.FindFirst(type);
            if (claim is not null)
            {
                claims.Add(new Claim(type, claim.Value));
            }
        }

        claims.Add(new Claim(SessionIdClaimType, sessionId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, nameType: "email", roleType: null));
    }
}
