using Xunit;

namespace MiroClone.Web.Tests.Canvas.Core;

/// <summary>
/// The canvas rules are plain C# (blazor-web-app "Logic outside the markup"). They are compiled into the Web assembly, so
/// the boundary is pinned by reading the sources: no Blazor, JS interop or component namespace may appear in Core.
/// </summary>
public sealed class CoreIsolationTests
{
    private static readonly string CoreDirectory = Path.Combine(FindRepoRoot(), "MiroClone.Web", "Canvas", "Core");

    [Fact]
    public void CoreSourcesDoNotReferenceBlazorOrJsInterop()
    {
        var forbidden = new[] { "Microsoft.AspNetCore", "Microsoft.JSInterop", "MiroClone.Web.Components", "@code", "@inject" };
        var files = Directory.GetFiles(CoreDirectory, "*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var term in forbidden)
            {
                Assert.False(text.Contains(term, StringComparison.Ordinal), $"{Path.GetFileName(file)} mentions {term}.");
            }
        }
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
