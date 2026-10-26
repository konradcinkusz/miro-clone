using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MiroClone.ServiceDefaults;

namespace MiroClone.Web.Account;

/// <summary>
/// The auth rate limit on the account forms (ADR 0045). The kernel's policy is the only limiter the web app uses, and it answers with
/// JSON. A form cannot read JSON, and the account endpoints answer every step with a redirect and a message, so a rejected form is
/// redirected to its own page with the same kind of message.
/// </summary>
public static class AccountRateLimiting
{
    public const string TooManyAttempts = "Too many attempts. Wait a minute, then try again.";

    /// <summary>The account form's page, by the endpoint it posts to. Sign-out is not limited, so it has no entry.</summary>
    private static readonly Dictionary<string, string> PageFor = new(StringComparer.Ordinal)
    {
        ["/account/sign-in"] = "/sign-in",
        ["/account/sign-up"] = "/sign-up",
        ["/account/verify-email"] = "/verify-email",
        ["/account/resend-verification"] = "/verify-email",
        ["/account/forgot-password"] = "/forgot-password",
        ["/account/reset-password"] = "/reset-password",
        ["/account/consent"] = "/consent",
    };

    public static IServiceCollection AddAccountRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStandardRateLimiting(configuration, globalLimit: false);
        services.Configure<RateLimiterOptions>(options =>
        {
            var kernelRejection = options.OnRejected;
            options.OnRejected = (context, cancellationToken) =>
            {
                if (PageFor.TryGetValue(context.HttpContext.Request.Path.Value ?? string.Empty, out var page))
                {
                    context.HttpContext.Response.Redirect(RejectedPage(page));
                    return ValueTask.CompletedTask;
                }

                return kernelRejection is null ? ValueTask.CompletedTask : kernelRejection(context, cancellationToken);
            };
        });
        return services;
    }

    /// <summary>The page the rejected form returns to, with the message in the query like every other account answer.</summary>
    public static string RejectedPage(string page) => $"{page}?error={Uri.EscapeDataString(TooManyAttempts)}";
}
