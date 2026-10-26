using System.Text.RegularExpressions;
using Xunit;

namespace MiroClone.Web.Tests.Style;

/// <summary>
/// The board cover pictures use only colours from the token contract, so a restyle that changes a token changes them too and no
/// stray colour sneaks into a picture. StyleContractTests covers the stylesheet; this covers the SVG files it does not read.
/// </summary>
public sealed class PlaceholderContractTests
{
    private static readonly string Root = FindRepoRoot();

    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "wwwroot", "app.css"));

    private static readonly HashSet<string> TokenColours = Regex.Matches(Stylesheet, @"--color-[a-z-]+:\s*(#[0-9a-fA-F]{6}|rgba\([^)]*\));")
        .Select(match => match.Groups[1].Value.ToLowerInvariant())
        .ToHashSet();

    private static readonly string BoardsFolder = Path.Combine(Root, "MiroClone.Web", "wwwroot", "boards");

    [Fact]
    public void EveryBoardCoverExistsForEachSeed()
    {
        for (var seed = 1; seed <= 10; seed++)
        {
            Assert.True(File.Exists(Path.Combine(BoardsFolder, $"seed-{seed}.svg")), $"seed-{seed}.svg is missing.");
        }
    }

    [Fact]
    public void NoBoardCoverUsesAColourOutsideTheTokens()
    {
        var files = Directory.GetFiles(BoardsFolder, "*.svg");
        Assert.Equal(10, files.Length);

        foreach (var file in files)
        {
            var colours = Regex.Matches(File.ReadAllText(file), @"#[0-9a-fA-F]{6}\b").Select(match => match.Value.ToLowerInvariant());
            foreach (var colour in colours)
            {
                Assert.True(TokenColours.Contains(colour), $"{Path.GetFileName(file)} uses {colour}, which is not a token colour.");
            }
        }
    }

    [Fact]
    public void NoBoardCoverNamesAColourByKeyword()
    {
        foreach (var file in Directory.GetFiles(BoardsFolder, "*.svg"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(@"(fill|stroke)=""(black|white|red|blue|gray|grey|green|yellow)""", text);
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
