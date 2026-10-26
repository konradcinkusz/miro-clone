using System.Diagnostics;
using MiroClone.ServiceDefaults;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>The span processor removes a bearer token from the URL tags before export (ADR 0047).</summary>
public sealed class QueryTokenRedactorTests
{
    private const string Token = "eyJhbGciOiJSUzI1NiJ9.payload.signature";

    [Fact]
    public void TheTokenIsRemovedFromEveryUrlTagAndTheRouteStaysReadable()
    {
        using var activity = new Activity("GET");
        var url = $"https://miroclone.example/hubs/canvas?id=abc&access_token={Token}&x=1";
        activity.SetTag("url.full", url);
        activity.SetTag("url.query", $"?access_token={Token}");
        activity.SetTag("http.url", url);

        new QueryTokenRedactor().OnEnd(activity);

        foreach (var name in new[] { "url.full", "url.query", "http.url" })
        {
            var value = Assert.IsType<string>(activity.GetTagItem(name));
            Assert.DoesNotContain(Token, value, StringComparison.Ordinal);
            Assert.Contains("access_token=REDACTED", value, StringComparison.Ordinal);
        }

        Assert.Equal("https://miroclone.example/hubs/canvas?id=abc&access_token=REDACTED&x=1", activity.GetTagItem("url.full"));
    }

    [Fact]
    public void AnUrlWithoutATokenIsLeftAlone()
    {
        using var activity = new Activity("GET");
        activity.SetTag("url.full", "https://miroclone.example/api/boards?page=2");

        new QueryTokenRedactor().OnEnd(activity);

        Assert.Equal("https://miroclone.example/api/boards?page=2", activity.GetTagItem("url.full"));
    }
}
