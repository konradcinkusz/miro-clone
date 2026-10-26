namespace MiroClone.Web.Security;

/// <summary>
/// Headers every response of the web app carries (ADR 0047). The app serves no page that must be framed, so nothing may frame it:
/// a framed sign-in form is a clickjacking target. The policy lets the page load its own script, styles and sockets and nothing else.
/// Inline style attributes are allowed because the board and the markup use them; there is no inline script.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; " +
        "font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            headers["X-Frame-Options"] = "DENY";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next(context);
        });
    }
}
