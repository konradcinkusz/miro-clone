using Xunit;

namespace MiroClone.Web.Tests.Canvas.Interop;

/// <summary>
/// The pointer module is the only JavaScript in the app (ADR 0026). These checks keep it inside its budget: at most 60 lines, no
/// imports, no network, no eval. They read the file, so a change that breaks the budget fails here before review.
/// </summary>
public sealed class CanvasPointerModuleTests
{
    private static readonly string Module = File.ReadAllText(Path.Combine(FindRepoRoot(), "MiroClone.Web", "wwwroot", "js", "canvas-pointer.js"));

    [Fact]
    public void TheModuleIsAtMostSixtyLines()
    {
        var lines = Module.Split('\n').Length;

        Assert.True(lines <= 60, $"canvas-pointer.js has {lines} lines; the budget is 60 (ADR 0026).");
    }

    [Fact]
    public void TheModuleExportsOnlyAttachAndImportsNothing()
    {
        Assert.Contains("export function attach(surface, target)", Module);
        Assert.DoesNotContain("import ", Module);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Module, @"^export\s", System.Text.RegularExpressions.RegexOptions.Multiline));
    }

    [Theory]
    [InlineData("fetch(")]
    [InlineData("XMLHttpRequest")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("localStorage")]
    public void TheModuleMakesNoNetworkCallsAndRunsNoStrings(string forbidden) =>
        Assert.DoesNotContain(forbidden, Module);

    [Fact]
    public void TheModuleSendsEveryPhaseAndTheWheelToTheCircuit()
    {
        foreach (var phase in new[] { "\"down\"", "\"move\"", "\"up\"", "\"cancel\"" })
        {
            Assert.Contains(phase, Module);
        }

        Assert.Contains("\"OnPointer\"", Module);
        Assert.Contains("\"OnWheel\"", Module);
        Assert.Contains("passive: false", Module);
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
