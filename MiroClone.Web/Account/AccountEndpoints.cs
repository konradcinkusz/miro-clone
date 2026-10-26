using MiroClone.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using MiroClone.ServiceDefaults;

namespace MiroClone.Web.Account;

/// <summary>
/// The account forms' POST targets. Each form is a plain HTML form (no circuit, no JavaScript), so these endpoints own the
/// cookie, the antiforgery check and the redirect. Every step answers with a redirect back to a page, carrying a message in the
/// query; no step answers with a bare error. Authentication is never decided here by the cookie alone: the session is in the store.
/// </summary>
/// <summary>Limits the request body of every endpoint in a group, before a handler reads it (ADR 0047).</summary>
public static class FormBodyLimit
{
    public static RouteGroupBuilder LimitFormBody(this RouteGroupBuilder group, long bytes) =>
        group.AddEndpointFilter(async (context, next) =>
        {
            // A declared length over the limit is refused before any read. Minimal APIs do not enforce IRequestSizeLimitMetadata, and
            // a body without a length (chunked) is capped by the server's feature, set here before the handler reads it.
            if (context.HttpContext.Request.ContentLength is { } declared && declared > bytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            if (context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = bytes;
            }

            return await next(context);
        });
}

public static class AccountEndpoints
{
    public const string FormExpired = "Your form expired. Submit it again.";

    /// <summary>The largest account form body. The forms carry an email, a password and two checkboxes; anything larger is refused
    /// before it is read, so an unauthenticated client cannot make the server buffer megabytes (ADR 0047).</summary>
    public const int MaximumFormBytes = 16 * 1024;

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The auth policy covers every form that checks a password or sends a message. Sign-out is left out: ending a session must
        // always work (ADR 0045).
        var account = endpoints.MapGroup("/account")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .LimitFormBody(MaximumFormBytes);
        account.MapPost("/sign-in", SignIn);
        account.MapPost("/sign-up", SignUp);
        account.MapPost("/verify-email", VerifyEmail);
        account.MapPost("/resend-verification", ResendVerification);
        account.MapPost("/forgot-password", ForgotPassword);
        account.MapPost("/reset-password", ResetPassword);
        account.MapPost("/consent", AcceptConsent);
        account.MapPost("/sign-out", SignOut).DisableRateLimiting();
        return endpoints;
    }

    private static async Task<IResult> SignIn(HttpContext http, IAntiforgery antiforgery, AccountFlows flows, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        var returnUrl = LocalReturnUrl.Resolve(form["returnUrl"]);
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(Page("/sign-in", FormExpired, returnUrl: returnUrl));
        }

        var outcome = await flows.SignInAsync(form["email"].ToString().Trim(), form["password"].ToString(), ct);
        if (outcome.Error is not null)
        {
            return Results.Redirect(Page("/sign-in", outcome.Error, returnUrl: returnUrl));
        }

        await SignInCookieAsync(http, outcome);
        return Results.Redirect(outcome.RequiresConsent ? Page("/consent", returnUrl: returnUrl) : returnUrl);
    }

    private static async Task<IResult> SignUp(HttpContext http, IAntiforgery antiforgery, AccountFlows flows, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(Page("/sign-up", FormExpired));
        }

        var email = form["email"].ToString().Trim();
        var outcome = await flows.SignUpAsync(
            email,
            form["password"].ToString(),
            termsAccepted: form["terms"] == "on",
            privacyAccepted: form["privacy"] == "on",
            ct);

        if (outcome.Error is not null)
        {
            return Results.Redirect(Page("/sign-up", outcome.Error));
        }

        if (outcome.VerificationRequired)
        {
            return Results.Redirect(QueryHelpers.AddQueryString("/verify-email", new Dictionary<string, string?> { ["email"] = email, ["pending"] = "1" }));
        }

        await SignInCookieAsync(http, outcome);
        return Results.Redirect(outcome.RequiresConsent ? Page("/consent") : LocalReturnUrl.Home);
    }

    private static async Task<IResult> VerifyEmail(HttpContext http, IAntiforgery antiforgery, AuthorityClient authority, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        var email = form["email"].ToString().Trim();
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(Page("/verify-email", FormExpired, email));
        }

        var verified = await authority.VerifyEmailAsync(email, form["token"].ToString().Trim(), ct);
        return verified.Succeeded
            ? Results.Redirect("/sign-in?verified=1")
            : Results.Redirect(Page("/verify-email", verified.Error!, email));
    }

    private static async Task<IResult> ResendVerification(HttpContext http, IAntiforgery antiforgery, AuthorityClient authority, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        var email = form["email"].ToString().Trim();
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(Page("/verify-email", FormExpired, email));
        }

        var sent = await authority.ResendVerificationAsync(email, ct);
        return sent.Succeeded
            ? Results.Redirect(QueryHelpers.AddQueryString("/verify-email", new Dictionary<string, string?> { ["email"] = email, ["resent"] = "1" }))
            : Results.Redirect(Page("/verify-email", sent.Error!, email));
    }

    private static async Task<IResult> ForgotPassword(HttpContext http, IAntiforgery antiforgery, AuthorityClient authority, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(Page("/forgot-password", FormExpired));
        }

        var sent = await authority.ForgotPasswordAsync(form["email"].ToString().Trim(), ct);
        return sent.Succeeded
            ? Results.Redirect("/forgot-password?sent=1")
            : Results.Redirect(Page("/forgot-password", sent.Error!));
    }

    private static async Task<IResult> ResetPassword(HttpContext http, IAntiforgery antiforgery, AuthorityClient authority, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        var email = form["email"].ToString().Trim();
        var token = form["token"].ToString().Trim();
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(Page("/reset-password", FormExpired, email, token));
        }

        var reset = await authority.ResetPasswordAsync(email, token, form["password"].ToString(), ct);
        return reset.Succeeded
            ? Results.Redirect("/sign-in?reset=1")
            : Results.Redirect(Page("/reset-password", reset.Error!, email, token));
    }

    private static async Task<IResult> AcceptConsent(HttpContext http, IAntiforgery antiforgery, AccountFlows flows, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        var returnUrl = LocalReturnUrl.Resolve(form["returnUrl"]);
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(Page("/consent", FormExpired, returnUrl: returnUrl));
        }

        if (form["terms"] != "on" || form["privacy"] != "on")
        {
            return Results.Redirect(Page("/consent", AccountFlows.TermsRequired, returnUrl: returnUrl));
        }

        var sessionId = SessionClaims.SessionId(http.User);
        if (sessionId is null)
        {
            return Results.Redirect(LocalReturnUrl.SignInFor(returnUrl));
        }

        var outcome = await flows.AcceptConsentAsync(sessionId, ct);
        return outcome.Error is null
            ? Results.Redirect(returnUrl)
            : Results.Redirect(Page("/consent", outcome.Error, returnUrl: returnUrl));
    }

    private static async Task<IResult> SignOut(HttpContext http, IAntiforgery antiforgery, AccountFlows flows, CancellationToken ct)
    {
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Redirect(LocalReturnUrl.Home);
        }

        await flows.EndAsync(SessionClaims.SessionId(http.User), ct);
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/sign-in");
    }

    private static Task SignInCookieAsync(HttpContext http, AccountOutcome outcome) =>
        http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            outcome.CookiePrincipal!,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });

    /// <summary>A page address with its message, the address the user typed, and the return URL, all as query values.</summary>
    private static string Page(string path, string? error = null, string? email = null, string? token = null, string? returnUrl = null)
    {
        var query = new Dictionary<string, string?>();
        if (error is not null)
        {
            query["error"] = error;
        }

        if (email is not null)
        {
            query["email"] = email;
        }

        if (token is not null)
        {
            query["token"] = token;
        }

        if (returnUrl is not null)
        {
            query["returnUrl"] = returnUrl;
        }

        return QueryHelpers.AddQueryString(path, query);
    }
}
