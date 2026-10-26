using Xunit;
using Microsoft.Playwright;
using MiroClone.Web.E2E.Browser;
using MiroClone.Web.E2E.Stack;

namespace MiroClone.Web.E2E.Journeys;

/// <summary>
/// The base of every journey: the estate, a fresh authservice client, and a helper that opens a signed-in browser. Each test
/// creates its own accounts and boards, so no test depends on another (testing-strategy §6).
/// </summary>
public abstract class JourneyTests(EstateFixture stack) : IDisposable
{
    private readonly HttpClient _http = new();

    protected EstateFixture Stack { get; } = stack;

    protected AuthorityApi Authority => new(_http, Stack);

    /// <summary>A browser signed in as the account, with its circuit ready.</summary>
    protected async Task<Journey> SignedInAsync(Account account, ViewportSize? viewport = null)
    {
        var journey = await Journey.OpenAsync(Stack, viewport);
        try
        {
            await journey.SignInAsync(account);
            await journey.OpenDashboardAsync();
            return journey;
        }
        catch
        {
            await journey.DisposeAsync();
            throw;
        }
    }

    protected static string Title(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(60, prefix.Length + 33)];

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
