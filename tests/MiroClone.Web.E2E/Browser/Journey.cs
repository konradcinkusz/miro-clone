using System.Text.RegularExpressions;
using Microsoft.Playwright;
using MiroClone.Web.E2E.Stack;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Browser;

/// <summary>
/// One browser context and its page, with the circuit watch attached. The helpers express what a person does (sign in, create a
/// board, draw a rectangle) through the test ids and roles the pages publish, and each interactive step waits for the circuit.
/// </summary>
public sealed partial class Journey : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly CircuitWatch _circuit;

    private Journey(IBrowserContext context, IPage page, CircuitWatch circuit, string baseUrl)
    {
        _context = context;
        Page = page;
        _circuit = circuit;
        BaseUrl = baseUrl;
    }

    public IPage Page { get; }

    public string BaseUrl { get; }

    public static async Task<Journey> OpenAsync(EstateFixture stack, ViewportSize? viewport = null, ColorScheme? colourScheme = null)
    {
        var context = await stack.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = stack.BaseUrl,
            ViewportSize = viewport ?? new ViewportSize { Width = 1280, Height = 800 },
            ColorScheme = colourScheme ?? ColorScheme.Light,
        });
        var page = await context.NewPageAsync();
        return new Journey(context, page, new CircuitWatch(page), stack.BaseUrl);
    }

    /// <summary>Opens an interactive page and waits until its circuit takes input.</summary>
    public async Task GoToInteractiveAsync(string path)
    {
        var ready = _circuit.ReadyCount;
        await Page.GotoAsync(path);
        await _circuit.WaitForReadyAsync(ready + 1);
    }

    /// <summary>Opens a page the server renders without a circuit (the account forms).</summary>
    public Task GoToStaticAsync(string path) => Page.GotoAsync(path);

    /// <summary>Signs in through the account form and waits for the dashboard's circuit.</summary>
    public async Task SignInAsync(Account account)
    {
        // The account pages are interactive-router pages (ADR 0042): the circuit takes over the form after the server render and
        // replaces what was typed before it connected, so the helper waits for the circuit before it types.
        await GoToInteractiveAsync("/sign-in");
        // The circuit's first render can still replace the prerendered form just after it reports ready (a slower estate, such as the Debug-built
        // AppHost, shows it): a typed value is lost, or a click lands on the element that is being replaced and no POST is sent. So each attempt
        // fills both fields, confirms them, clicks, and waits a short while for the dashboard; an attempt that changed nothing is repeated.
        var ready = _circuit.ReadyCount;
        for (var attempt = 1; ; attempt++)
        {
            await FillFieldAsync("#sign-in-email", account.Email);
            await FillFieldAsync("#sign-in-password", account.Password);
            if (await Page.Locator("#sign-in-email").InputValueAsync() != account.Email
                || await Page.Locator("#sign-in-password").InputValueAsync() != account.Password)
            {
                if (attempt == 4)
                {
                    throw new InvalidOperationException($"The sign-in form kept losing what was typed. {_circuit.Report()}");
                }

                continue;
            }

            await Page.GetByTestId("sign-in-submit").ClickAsync();
            try
            {
                await Expect(Page).ToHaveURLAsync(new Regex("/$"), new PageAssertionsToHaveURLOptions { Timeout = attempt == 4 ? 15_000 : 6_000 });
                break;
            }
            catch (PlaywrightException error) when (attempt < 4)
            {
                Console.WriteLine($"sign-in attempt {attempt} as {account.Email} stayed on {Page.Url}: {error.Message.Split('\n')[0]}");
            }
            catch (PlaywrightException error)
            {
                throw new InvalidOperationException($"Sign-in as {account.Email} did not reach the dashboard after {attempt} attempts. {_circuit.Report()}", error);
            }
        }

        await _circuit.WaitForReadyAsync(ready + 1);
        await Expect(Page.GetByTestId("signed-in-email")).ToHaveTextAsync(account.Email);
    }

    /// <summary>
    /// Fills a form field by its element id, the id the account pages publish. On a timeout the page's address and markup go to the
    /// log before the failure is rethrown, so a missing field shows what the page actually rendered.
    /// </summary>
    public async Task FillFieldAsync(string selector, string value)
    {
        try
        {
            await Page.Locator(selector).FillAsync(value, new LocatorFillOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            var html = await Page.ContentAsync();
            Console.WriteLine($"--- {selector} not found at {Page.Url} (title: {await Page.TitleAsync()}) ---");
            Console.WriteLine(html.Length > 6000 ? html[..6000] : html);
            throw;
        }
    }

    /// <summary>Opens the dashboard for a signed-in user and waits until its heading shows.</summary>
    public async Task OpenDashboardAsync()
    {
        await GoToInteractiveAsync("/");
        await Expect(Page.GetByTestId("dashboard-heading")).ToBeVisibleAsync();
    }

    /// <summary>Creates a board from the dashboard and returns its id once the board page shows its title.</summary>
    public async Task<Guid> CreateBoardAsync(string title)
    {
        // A click that lands before the dashboard's interactive render settles opens nothing, so the click is repeated until the title box shows.
        for (var attempt = 1; ; attempt++)
        {
            await Page.GetByTestId("new-board").Or(Page.GetByTestId("empty-create")).ClickAsync();
            try
            {
                await Expect(Page.GetByTestId("board-title-input")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = attempt == 4 ? 15_000 : 5_000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 4)
            {
                Console.WriteLine($"new-board attempt {attempt}: the title box did not open on {Page.Url}");
            }
        }

        await Page.GetByTestId("board-title-input").FillAsync(title);
        await Page.GetByTestId("board-title-submit").ClickAsync();
        await Expect(Page).ToHaveURLAsync(BoardUrl());
        await Expect(Page.GetByTestId("board-title")).ToHaveTextAsync(title);
        return BoardId();
    }

    /// <summary>The page's board id, read from the address.</summary>
    public Guid BoardId()
    {
        var match = BoardUrl().Match(Page.Url);
        return match.Success ? Guid.Parse(match.Groups["id"].Value) : Guid.Empty;
    }

    public static Regex BoardUrl() => new(@"/board/(?<id>[0-9a-fA-F-]{36})$");

    public async ValueTask DisposeAsync()
    {
        _circuit.Dispose();
        await _context.CloseAsync();
    }
}
