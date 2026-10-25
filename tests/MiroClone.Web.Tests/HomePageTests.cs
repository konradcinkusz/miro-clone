using Bunit;
using MiroClone.Web.Components.Pages;
using Xunit;

namespace MiroClone.Web.Tests;

public sealed class HomePageTests : IDisposable
{
    private readonly BunitContext _context = new();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void RootRendersHello()
    {
        var page = _context.Render<Home>();

        Assert.Equal("Hello", page.Find("h1").TextContent);
    }
}
