using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MiroClone.ServiceDefaults;
using MiroClone.Web.Api;
using MiroClone.Web.Auth;
using MiroClone.Web.Canvas.Interop;
using MiroClone.Web.Canvas.Realtime;

namespace MiroClone.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configuration key for the Data Protection key ring (docs/decisions/0019-web-data-protection-keys-on-a-volume.md). Set to /data/keys on Fly; defaults to
    /// App_Data/keys under the content root. The ring is always persisted, so a restart does not sign anyone out.
    /// </summary>
    public const string DataProtectionKeysPathKey = "DataProtection:KeysPath";

    /// <summary>
    /// The web app's services: interactive Server components, cookie sign-in with the session store behind it, the typed
    /// clients for the services, and the persisted Data Protection keys (blazor-web-app §4, §7, §14). Configuration is read from
    /// the same "Auth" section the services use, so the estate has one authority, issuer and audience.
    /// </summary>
    public static IServiceCollection AddWebApp(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddAuthorization();
        services.AddDistributedMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<SessionSettings>(configuration.GetSection(SessionSettings.SectionName));

        AddSessionAuthentication(services, configuration, environment);
        AddDataProtection(services, configuration, environment);
        AddAuthority(services, configuration);
        AddServiceClients(services);

        return services;
    }

    private static void AddSessionAuthentication(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var lifetime = configuration.GetSection(SessionSettings.SectionName).Get<SessionSettings>()?.Lifetime ?? TimeSpan.FromHours(8);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                // The cookie names a session; it is never a credential. HttpOnly and SameSite=Strict keep it from script and from
                // cross-site requests. Secure is required everywhere but the developer machine, which is plain http.
                options.Cookie.Name = "miroclone.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = lifetime;
                options.SlidingExpiration = false;
                options.LoginPath = "/sign-in";
                options.ReturnUrlParameter = "returnUrl";
            });

        services.AddScoped<AuthenticationStateProvider, SessionRevalidatingAuthenticationStateProvider>();
        services.AddScoped<ICurrentSession, CircuitSession>();
        services.AddSingleton<SessionService>();
    }

    private static void AddDataProtection(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var keyDirectory = configuration[DataProtectionKeysPathKey]
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "keys");

        // Cookies and antiforgery tokens are encrypted with these keys. Persisted to a directory (the volume in deployment), so
        // a restart or a second deploy does not invalidate open forms or sign-ins (blazor-web-app §14).
        services.AddDataProtection()
            .SetApplicationName("MiroClone.Web")
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
    }

    private static void AddAuthority(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(JwtValidationOptions.SectionName).Get<JwtValidationOptions>() ?? new JwtValidationOptions();
        if (!Uri.TryCreate(settings.Authority, UriKind.Absolute, out var authority))
        {
            throw new InvalidOperationException("Auth:Authority must be an absolute URL.");
        }

        services.AddSingleton(settings);
        services.AddSingleton(_ => AccessTokenValidator.DiscoveryFor(settings));
        services.AddSingleton<AccessTokenValidator>();
        services.AddTransient<AccountFlows>();

        // Resolved from a scope when a session refreshes (SessionService). The service defaults add the one resilience handler.
        services.AddHttpClient<AuthorityClient>(http => http.BaseAddress = new Uri(authority.ToString().TrimEnd('/') + "/"))
            .AddHttpMessageHandler<BearerTokenHandler>();
    }

    private static void AddServiceClients(IServiceCollection services)
    {
        // Logical names, resolved by Aspire's service discovery (blazor-web-app §5). Each carries the bearer handler.
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient<BoardsClient>(http => http.BaseAddress = new Uri("http://boards-service/"))
            .AddHttpMessageHandler<BearerTokenHandler>();
        services.AddHttpClient<CanvasClient>(http => http.BaseAddress = new Uri("http://canvas-service/"))
            .AddHttpMessageHandler<BearerTokenHandler>();

        // The board surface's pointer input (ADR 0030). One per circuit; the surface disposes it with the page.
        services.AddTransient<ICanvasPointerSource, JsCanvasPointerSource>();

        // The live board session: a SignalR connection from the circuit to the canvas hub (WP10, ADR 0035). One per opened board.
        services.AddTransient<ICanvasSessionFactory, HubCanvasSessionFactory>();
    }
}
