using Bunit;
using MiroClone.Web.Canvas.Realtime;
using MiroClone.Web.Components.Board;
using Xunit;

namespace MiroClone.Web.Tests.Components.Board;

/// <summary>The banner says what state the board is in, including the notice that new edits are paused (ADR 0035).</summary>
public sealed class ConnectionBannerTests : BunitContext
{
    [Fact]
    public void ItIsHiddenWhileTheBoardIsLive()
    {
        var cut = Render<ConnectionBanner>(parameters => parameters.Add(banner => banner.State, CanvasConnectionState.Live));

        Assert.Empty(cut.FindAll("[data-testid='connection-banner']"));
    }

    [Fact]
    public void ItSaysNewEditsArePausedWhenTheOutboxIsFullEvenWhileLive()
    {
        var cut = Render<ConnectionBanner>(parameters => parameters
            .Add(banner => banner.State, CanvasConnectionState.Live)
            .Add(banner => banner.EditsPaused, true));

        var banner = cut.Find("[data-testid='connection-banner']");
        Assert.Contains("new edits are paused", banner.TextContent, StringComparison.Ordinal);
        Assert.Equal("callout callout-warning", banner.GetAttribute("class"));
    }

    [Fact]
    public void WhileReconnectingItSaysTheEditsStayOnScreen()
    {
        var cut = Render<ConnectionBanner>(parameters => parameters.Add(banner => banner.State, CanvasConnectionState.Reconnecting));

        var banner = cut.Find("[data-testid='connection-banner']");
        Assert.Equal("reconnecting", banner.GetAttribute("data-state"));
        Assert.Contains("your edits stay on this screen", banner.TextContent, StringComparison.Ordinal);
    }
}
