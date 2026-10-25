using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>
/// Pins the host's structure by reading it from disk (blazor-web-app §3, §13). Without the render mode on the root
/// components every page is static SSR, and bUnit cannot see that because it renders components, not the root.
/// </summary>
public sealed class RenderModePinTests
{
    private static readonly string Root = FindRepoRoot();
    private static readonly string App = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "Components", "App.razor"));
    private static readonly string Program = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "Program.cs"));

    [Fact]
    public void HeadOutletCarriesTheInteractiveServerRenderMode()
    {
        Assert.Contains("<HeadOutlet @rendermode=\"InteractiveServer\" />", App);
    }

    [Fact]
    public void RoutesCarriesTheInteractiveServerRenderMode()
    {
        Assert.Contains("<Routes @rendermode=\"InteractiveServer\" />", App);
    }

    [Fact]
    public void ScopedStylesBundleIsLinked()
    {
        Assert.Contains("@Assets[\"MiroClone.Web.styles.css\"]", App);
    }

    [Fact]
    public void TheVisualTokenStylesheetIsLinkedBeforeTheScopedStyles()
    {
        var tokens = App.IndexOf("@Assets[\"app.css\"]", StringComparison.Ordinal);
        var scoped = App.IndexOf("@Assets[\"MiroClone.Web.styles.css\"]", StringComparison.Ordinal);

        Assert.True(tokens >= 0 && tokens < scoped, "app.css must be linked, ahead of the scoped bundle, or no page is styled.");
    }

    [Fact]
    public void HostRegistersInteractiveServerAndNoWebAssembly()
    {
        Assert.Contains("AddInteractiveServerComponents()", File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "ServiceCollectionExtensions.cs")));
        Assert.Contains("AddInteractiveServerRenderMode()", Program);
        Assert.DoesNotContain("WebAssembly", Program);
        Assert.DoesNotContain("blazor.webassembly", App);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MiroClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("MiroClone.sln was not found above the test output.");
    }
}
