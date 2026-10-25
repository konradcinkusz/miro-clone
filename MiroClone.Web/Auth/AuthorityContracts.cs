namespace MiroClone.Web.Auth;

/// <summary>
/// Tokens authservice issued for one session. A copy of its TokenResponse: only the fields this app reads (ADR 0011).
/// </summary>
public sealed record AuthorityTokens(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>The consent document versions authservice currently requires (GET api/v1/auth/consents/versions).</summary>
public sealed record ConsentVersions(string Terms, string Privacy, string Cookies);

/// <summary>An organization the user belongs to, as the workspace list shows it. Role is authservice's "userRole" (its OrganizationDto).</summary>
public sealed record AuthorityOrganization(string Id, string Name, string Role);

/// <summary>The result of one authservice call: a value, or the message to show the user. Status is the HTTP status.</summary>
public sealed record AuthorityResult<T>(T? Value, string? Error, int Status)
{
    public bool Succeeded => Error is null;
}

/// <summary>Factories for <see cref="AuthorityResult{T}"/>; kept outside the generic type (CA1000).</summary>
public static class AuthorityResult
{
    public static AuthorityResult<T> Success<T>(T value) => new(value, null, 200);

    public static AuthorityResult<T> Failure<T>(string message, int status) => new(default, message, status);
}
