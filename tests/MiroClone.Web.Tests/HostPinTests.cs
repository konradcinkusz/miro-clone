using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>
/// Structural facts the web host must keep, read from disk (blazor-web-app §3, §7, §8, §13): the order of the pipeline, the
/// authorization of the dashboard, and that no interactive component reads the HTTP context once the circuit runs.
/// </summary>
public sealed class HostPinTests
{
    private static readonly string Root = FindRepoRoot();
    private static readonly string[] ExpectedTokenHolders = ["AccountEndpoints.cs"];
    private static readonly string Program = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "Program.cs"));

    [Fact]
    public void TheOriginGuardRunsBeforeAntiforgeryAndTheComponents()
    {
        var guard = Program.IndexOf("app.UseOriginGuard();", StringComparison.Ordinal);
        var antiforgery = Program.IndexOf("app.UseAntiforgery();", StringComparison.Ordinal);
        var components = Program.IndexOf("app.MapRazorComponents<App>()", StringComparison.Ordinal);

        Assert.True(guard >= 0 && guard < antiforgery && antiforgery < components);
    }

    [Fact]
    public void AuthenticationRunsBeforeTheAccountEndpointsAndTheComponents()
    {
        var authentication = Program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var account = Program.IndexOf("app.MapAccountEndpoints();", StringComparison.Ordinal);
        var components = Program.IndexOf("app.MapRazorComponents<App>()", StringComparison.Ordinal);

        Assert.True(authentication >= 0 && authentication < account && account < components);
    }

    [Fact]
    public void TheRoutesAuthorizeThePagesAndSendTheVisitorToSignIn()
    {
        var routes = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "Components", "Routes.razor"));

        Assert.Contains("<AuthorizeRouteView", routes);
        Assert.Contains("<NotAuthorized>", routes);
        Assert.Contains("<RedirectToSignIn />", routes);
    }

    [Fact]
    public void TheDashboardCarriesTheAuthorizeAttributeAsUxOnly()
    {
        var home = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "Components", "Pages", "Home.razor"));

        Assert.Contains("@attribute [Authorize]", home);
    }

    [Fact]
    public void ThePagesAndComponentsThatRunInTheCircuitNeverReadTheHttpContext()
    {
        var circuitFiles = Directory.GetFiles(Path.Combine(Root, "MiroClone.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("Pages", "Account"), StringComparison.Ordinal));

        foreach (var file in circuitFiles)
        {
            Assert.DoesNotContain("HttpContext", File.ReadAllText(file));
        }
    }

    [Fact]
    public void TheAccountPagesAreOutsideTheInteractiveRouterSoTheirFormsPostNatively()
    {
        var pages = Directory.GetFiles(Path.Combine(Root, "MiroClone.Web", "Components", "Pages", "Account"), "*.razor");

        Assert.NotEmpty(pages);
        foreach (var page in pages)
        {
            Assert.Contains("@attribute [ExcludeFromInteractiveRouting]", File.ReadAllText(page));
        }
    }

    [Fact]
    public void DataProtectionKeysArePersistedAndTheKeyFolderIsNotCommitted()
    {
        var registration = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "ServiceCollectionExtensions.cs"));
        var ignore = File.ReadAllText(Path.Combine(Root, ".gitignore"));

        Assert.Contains("PersistKeysToFileSystem", registration);
        Assert.Contains("App_Data/", ignore);
    }

    [Fact]
    public void TheWebAppTakesTokensOnlyThroughTheServerSideSession()
    {
        // The browser gets a cookie that names a session. Tokens are written only by the session store.
        var sources = Directory.GetFiles(Path.Combine(Root, "MiroClone.Web"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("obj", string.Empty), StringComparison.Ordinal)
                && !file.Contains(Path.Combine("bin", string.Empty), StringComparison.Ordinal));
        var holdingTokens = sources.Where(file => File.ReadAllText(file).Contains("AuthenticationProperties", StringComparison.Ordinal)
            || File.ReadAllText(file).Contains("StoreTokens", StringComparison.Ordinal)).Select(Path.GetFileName).ToArray();

        Assert.Equal(ExpectedTokenHolders, holdingTokens);
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
