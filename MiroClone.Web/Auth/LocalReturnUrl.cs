namespace MiroClone.Web.Auth;

/// <summary>
/// Where to send the user after sign-in. A return URL is taken only when it is a path on this site, so the sign-in form
/// cannot be used to redirect the user to another origin (an open redirect).
/// </summary>
public static class LocalReturnUrl
{
    public const string Home = "/";

    public static string Resolve(string? candidate)
    {
        if (candidate is not { Length: > 1 } path || path[0] != '/')
        {
            return Home;
        }

        // "//host" and "/\host" are protocol-relative to browsers; control characters are never part of a path.
        if (path[1] is '/' or '\\' || path.Any(char.IsControl))
        {
            return Home;
        }

        return path;
    }

    /// <summary>The sign-in page address that returns to <paramref name="path"/> after sign-in.</summary>
    public static string SignInFor(string? path) => "/sign-in?returnUrl=" + Uri.EscapeDataString(Resolve(path));
}
