using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MiroClone.Web.Api;
using MiroClone.Web.Auth;

namespace MiroClone.Web.Tests.TestSupport;

/// <summary>
/// The real session stack, wired as the web app wires it, with authservice and the services behind scripted handlers.
/// Only the network is replaced; the token validation, the session store, the refresh rules and the handler chain are real.
/// </summary>
internal sealed class AuthHarness : IDisposable
{
    public ScriptedHandler Authority { get; } = new();

    public ScriptedHandler Boards { get; } = new();

    public ManualClock Clock { get; } = new(DateTimeOffset.UtcNow);

    private readonly ServiceProvider _provider;

    public AuthHarness(SessionSettings? settings = null)
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Options.Create(settings ?? new SessionSettings()));
        services.AddSingleton(TestTokens.ValidationOptions());
        services.AddSingleton(TestTokens.Discovery());
        services.AddSingleton<AccessTokenValidator>();
        services.AddSingleton<SessionService>();
        services.AddTransient<AccountFlows>();
        services.AddTransient<BearerTokenHandler>();

        services.AddHttpClient<AuthorityClient>(http => http.BaseAddress = new Uri(TestTokens.Authority + "/"))
            .ConfigurePrimaryHttpMessageHandler(() => Authority)
            .AddHttpMessageHandler<BearerTokenHandler>();

        services.AddHttpClient<BoardsClient>(http => http.BaseAddress = new Uri("http://boards-service/"))
            .ConfigurePrimaryHttpMessageHandler(() => Boards)
            .AddHttpMessageHandler<BearerTokenHandler>();

        _provider = services.BuildServiceProvider();
    }

    public SessionService Sessions => _provider.GetRequiredService<SessionService>();

    public AccountFlows Flows => _provider.GetRequiredService<AccountFlows>();

    public AuthorityClient AuthorityClient => _provider.GetRequiredService<AuthorityClient>();

    public BoardsClient BoardsClient => _provider.GetRequiredService<BoardsClient>();

    /// <summary>A session stored directly, as a sign-in would leave it, for tests that start after sign-in.</summary>
    public async Task<string> StoreSessionAsync(string? accessToken = null, string refreshToken = "refresh-1", int expiresIn = 900)
    {
        var token = accessToken ?? TestTokens.Issue(organizations: ["org-1"]);
        var principal = await _provider.GetRequiredService<AccessTokenValidator>().ValidateAsync(token);
        return await Sessions.CreateAsync(new AuthorityTokens(token, refreshToken, expiresIn), principal);
    }

    public void Dispose() => _provider.Dispose();
}
