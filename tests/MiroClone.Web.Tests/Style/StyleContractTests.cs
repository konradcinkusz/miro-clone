using System.Text.RegularExpressions;
using Xunit;

namespace MiroClone.Web.Tests.Style;

/// <summary>
/// The visual contract (blazor-web-app §9) read from disk: the token blocks, both themes, the shared class names, the contrast
/// of every text pair in both themes, the focus ring, reduced motion, phone width, and no colour outside the tokens.
/// </summary>
public sealed class StyleContractTests
{
    private static readonly string Root = FindRepoRoot();
    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(Root, "MiroClone.Web", "wwwroot", "app.css"));

    private static readonly string Tokens = Between(Stylesheet, "/* tokens:start */", "/* tokens:end */");

    private static readonly string Light = Block(Tokens, ":root {");
    private static readonly string DarkMedia = Block(Tokens, "@media (prefers-color-scheme: dark)");
    private static readonly string DarkAttribute = Block(Tokens, ":root[data-theme=\"dark\"] {");

    private static readonly string[] NamedColours =
    [
        "black", "white", "red", "green", "blue", "gray", "grey", "silver", "navy", "teal", "maroon", "purple",
        "yellow", "orange", "pink", "brown", "lime", "aqua", "cyan", "magenta", "olive", "fuchsia",
    ];

    [Fact]
    public void TheTokenBlocksAreDelimited()
    {
        Assert.Contains("/* tokens:start */", Stylesheet);
        Assert.Contains("/* tokens:end */", Stylesheet);
        Assert.NotEmpty(Light);
    }

    [Fact]
    public void BothDarkThemeBlocksDefineEveryColourTokenTheLightThemeDefines()
    {
        var lightTokens = ColourTokens(Light);

        Assert.NotEmpty(lightTokens);
        Assert.Equal(lightTokens.Keys.Order(), ColourTokens(DarkMedia).Keys.Order());
        Assert.Equal(lightTokens.Keys.Order(), ColourTokens(DarkAttribute).Keys.Order());
    }

    [Fact]
    public void TheTwoDarkThemesAgree()
    {
        Assert.Equal(ColourTokens(DarkMedia), ColourTokens(DarkAttribute));
    }

    [Fact]
    public void TheDarkMediaThemeOnlyAppliesWhenTheUserChoseNoTheme()
    {
        Assert.Contains(":root:not([data-theme=\"light\"])", DarkMedia);
    }

    [Fact]
    public void BodyHasAnExplicitBackgroundAndColour()
    {
        var body = Block(Stylesheet, "html,\nbody {");
        Assert.Contains("background: var(--color-surface);", body);
        Assert.Contains("color: var(--color-text);", body);
    }

    [Theory]
    [InlineData(".card")]
    [InlineData(".stack")]
    [InlineData(".cluster")]
    [InlineData(".field")]
    [InlineData(".btn")]
    [InlineData(".btn-primary")]
    [InlineData(".btn-quiet")]
    [InlineData(".badge")]
    [InlineData(".callout")]
    [InlineData(".callout-danger")]
    [InlineData(".table-wrap")]
    [InlineData(".num")]
    [InlineData(".empty-state")]
    public void TheSharedClassNamesInTheContractExist(string selector)
    {
        Assert.Matches(Regex.Escape(selector) + @"\s*[{,\s:.]", Stylesheet);
    }

    [Fact]
    public void NoColourAppearsOutsideTheTokenBlocks()
    {
        var outside = Stylesheet.Replace(Tokens, string.Empty);
        AssertNoColours(outside, "app.css");

        foreach (var scoped in Directory.GetFiles(Path.Combine(Root, "MiroClone.Web"), "*.razor.css", SearchOption.AllDirectories))
        {
            AssertNoColours(File.ReadAllText(scoped), Path.GetFileName(scoped));
        }
    }

    [Theory]
    [InlineData("--color-text", "--color-surface", 4.5)]
    [InlineData("--color-text-secondary", "--color-surface", 4.5)]
    [InlineData("--color-text-muted", "--color-surface", 4.5)]
    [InlineData("--color-accent", "--color-surface", 4.5)]
    [InlineData("--color-accent-contrast", "--color-accent", 4.5)]
    [InlineData("--color-text", "--color-surface-raised", 4.5)]
    [InlineData("--color-text", "--color-accent-soft", 4.5)]
    [InlineData("--color-success", "--color-success-soft", 4.5)]
    [InlineData("--color-warning", "--color-warning-soft", 4.5)]
    [InlineData("--color-danger", "--color-danger-soft", 4.5)]
    [InlineData("--color-text", "--color-danger-soft", 4.5)]
    [InlineData("--color-focus", "--color-surface", 3.0)]
    [InlineData("--color-border", "--color-surface", 3.0)]
    public void TextAndControlPairsMeetWcagAaInBothThemes(string foreground, string background, double minimum)
    {
        foreach (var theme in new[] { ColourTokens(Light), ColourTokens(DarkAttribute) })
        {
            var ratio = Contrast(theme[foreground], theme[background]);
            Assert.True(ratio >= minimum, $"{foreground} on {background} is {ratio:F2}:1; AA needs {minimum}:1.");
        }
    }

    [Fact]
    public void TheFocusRingIsVisibleAndTheMotionIsReducedOnRequest()
    {
        Assert.Contains(":focus-visible", Stylesheet);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", Stylesheet);
        Assert.Contains("transition: none", Stylesheet);
    }

    [Fact]
    public void PhoneWidthHasAGutterAndNoPageScroll()
    {
        Assert.Contains("--gutter: 16px;", Tokens);
        Assert.Contains("overflow-x: auto;", Block(Stylesheet, ".table-wrap {"));
        Assert.DoesNotContain("width: 100vw", Stylesheet);
    }

    private static void AssertNoColours(string css, string name)
    {
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", css);
        Assert.DoesNotMatch(@"\b(rgba?|hsla?|hwb|lab|lch|oklch|oklab|color-mix)\(", css);
        foreach (var named in NamedColours)
        {
            Assert.DoesNotMatch(@"(:|,)\s*" + named + @"\b", css);
        }

        // Every colour property (color, border-color, background-color, ...) takes a token, or one of the keywords that carry no colour.
        foreach (Match declaration in Regex.Matches(css, @"(?:^|[\s{;])((?:[a-z]+-)*color)\s*:\s*([^;}]+)", RegexOptions.Multiline))
        {
            var value = declaration.Groups[2].Value.Trim();
            var allowed = value.StartsWith("var(", StringComparison.Ordinal) || value is "inherit" or "currentcolor" or "transparent";
            Assert.True(allowed, $"{name} sets {declaration.Groups[1].Value} to '{value}' outside a token.");
        }
    }

    private static Dictionary<string, string> ColourTokens(string block) =>
        Regex.Matches(block, @"(--color-[a-z-]+):\s*([^;]+);")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Trim());

    private static double Contrast(string foreground, string background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var value = hex.TrimStart('#');
        double Channel(int offset)
        {
            var c = Convert.ToInt32(value.Substring(offset, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"'{opening}' was not found.");
        }

        var depth = 0;
        for (var i = css.IndexOf('{', start); i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}' && --depth == 0)
            {
                return css[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"'{opening}' is not closed.");
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        return text[from..text.IndexOf(end, from, StringComparison.Ordinal)];
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
