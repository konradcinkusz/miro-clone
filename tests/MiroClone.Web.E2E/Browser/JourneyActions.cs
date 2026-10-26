using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MiroClone.Web.E2E.Browser;

/// <summary>The dashboard and board actions a journey composes. Every one locates by test id or by role and name.</summary>
public sealed partial class Journey
{
    /// <summary>Reloads an interactive page and waits for its new circuit, since a reload starts a circuit again.</summary>
    public async Task ReloadInteractiveAsync()
    {
        var ready = _circuit.ReadyCount;
        await Page.ReloadAsync();
        await _circuit.WaitForReadyAsync(ready + 1);
    }

    public async Task BackToDashboardAsync()
    {
        await Page.GetByTestId("back-to-boards").ClickAsync();
        await Expect(Page.GetByTestId("dashboard-heading")).ToBeVisibleAsync();
    }

    /// <summary>The card of a board by its title. Titles carry a unique suffix, so the text match is exact in practice.</summary>
    public ILocator Card(string title) =>
        Page.GetByTestId("board-card").Filter(new LocatorFilterOptions { HasText = title });

    public ILocator FavouriteToggle(string title) =>
        Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Favourite {title}" });

    /// <summary>Types a search, which the dashboard applies after its debounce.</summary>
    public async Task SearchAsync(string text)
    {
        await Page.GetByTestId("board-search").FillAsync(text);
    }

    public async Task RenameBoardAsync(string title, string newTitle)
    {
        await OpenCardMenuAsync(title);
        await Page.GetByTestId("board-rename").ClickAsync();
        await Page.GetByTestId("board-title-input").FillAsync(newTitle);
        await Page.GetByTestId("board-title-submit").ClickAsync();
    }

    public async Task DeleteBoardAsync(string title)
    {
        await OpenCardMenuAsync(title);
        await Page.GetByTestId("board-delete").ClickAsync();
        await Page.GetByTestId("confirm-delete-submit").ClickAsync();
    }

    private async Task OpenCardMenuAsync(string title)
    {
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Actions for {title}" }).ClickAsync();
        await Expect(Page.GetByTestId("board-menu")).ToBeVisibleAsync();
    }

    /// <summary>Selects the rectangle tool and drags one rectangle across the canvas surface, in board-relative pixels.</summary>
    public async Task DrawRectangleAsync(float fromX, float fromY, float toX, float toY)
    {
        await Page.GetByTestId("tool-rectangle").ClickAsync();
        var surface = Page.GetByTestId("canvas-surface");
        var box = await surface.BoundingBoxAsync() ?? throw new InvalidOperationException("The canvas surface has no box.");
        await Page.Mouse.MoveAsync(box.X + fromX, box.Y + fromY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + toX, box.Y + toY, new MouseMoveOptions { Steps = 8 });
        await Page.Mouse.UpAsync();
    }

    /// <summary>Moves the pointer across the middle of the canvas, which the presence channel reports to the other participants.</summary>
    public async Task MovePointerOverCanvasAsync()
    {
        var box = await Page.GetByTestId("canvas-surface").BoundingBoxAsync() ?? throw new InvalidOperationException("The canvas surface has no box.");
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new MouseMoveOptions { Steps = 10 });
    }

    /// <summary>Waits until the page's script reports a value, for checks the web-first assertions do not cover (focus, computed style).</summary>
    public async Task<T> WaitForScriptAsync<T>(string expression, Func<T, bool> until, string description)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var value = await Page.EvaluateAsync<T>(expression);
            if (until(value) || DateTimeOffset.UtcNow >= deadline)
            {
                return until(value) ? value : throw new TimeoutException($"{description} was {value} after 10 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
