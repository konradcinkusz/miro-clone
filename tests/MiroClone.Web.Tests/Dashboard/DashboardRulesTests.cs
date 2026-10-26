using System.Net;
using MiroClone.Web.Components.Dashboard;
using Xunit;

namespace MiroClone.Web.Tests.Dashboard;

/// <summary>The dashboard's rules, which need no renderer: the list state, the title, the card text and the error words.</summary>
public sealed class DashboardRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnEmptySearchSendsNoFilterAndAPaddedOneIsTrimmed()
    {
        Assert.Null(BoardQuery.Search("   "));
        Assert.Null(BoardQuery.Search(null));
        Assert.Equal("roadmap", BoardQuery.Search("  roadmap \t"));
    }

    [Fact]
    public void ThePageNeverGoesBelowTheFirst()
    {
        Assert.Equal(1, BoardQuery.Page(null));
        Assert.Equal(1, BoardQuery.Page(0));
        Assert.Equal(1, BoardQuery.Page(-4));
        Assert.Equal(3, BoardQuery.Page(3));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(45, 3)]
    public void ThePageCountRoundsUpAndAnEmptyListStillHasOnePage(int total, int pages) =>
        Assert.Equal(pages, BoardQuery.PageCount(total));

    [Fact]
    public void PreviousAndNextFollowThePageCount()
    {
        Assert.False(BoardQuery.HasPrevious(1));
        Assert.True(BoardQuery.HasPrevious(2));
        Assert.True(BoardQuery.HasNext(2, total: 45));
        Assert.False(BoardQuery.HasNext(3, total: 45));
    }

    [Fact]
    public void TheAddressOmitsDefaultsAndEscapesTheSearch()
    {
        Assert.Equal("/", BoardQuery.Url(null, favourites: false, page: 1));
        Assert.Equal("/?favourites=true", BoardQuery.Url(null, favourites: true, page: 1));
        Assert.Equal("/?search=a%26b%20c&page=2", BoardQuery.Url("a&b c", favourites: false, page: 2));
        Assert.Equal("/", BoardQuery.Url("   ", favourites: false, page: 1));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    public void TheTitleRuleAcceptsOneToSixtyCharactersAfterTrimming(int length, bool accepted)
    {
        var title = new string('a', length);
        Assert.Equal(accepted, BoardTitle.Normalize("  " + title + "  ") is not null);
        Assert.Equal(accepted, BoardTitle.Error(title) is null);
    }

    [Fact]
    public void TheTitleMessageNamesTheLimit() =>
        Assert.Equal("Enter a title of 1 to 60 characters.", BoardTitle.Error(new string('x', 61)));

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("  Roadmap  ", "Roadmap")]
    public void TheTitleIsTrimmedOrRefused(string? raw, string? expected) =>
        Assert.Equal(expected, BoardTitle.Normalize(raw));

    [Theory]
    [InlineData("", null)]
    [InlineData("  x ", "x")]
    [InlineData(" Sketch ", "Sketch")]
    [InlineData("  A B  C ", "A B  C")]
    public void TheWebTitleRuleAgreesWithTheBoardsServiceRule(string? raw, string? expected)
    {
        // The boards service answers 400 for what this rule refuses; the two must not drift.
        Assert.Equal(expected, BoardTitle.Normalize(raw));
        Assert.Equal(MiroClone.BoardsService.Boards.BoardTitle.Normalize(raw), BoardTitle.Normalize(raw));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(-30, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(5 * 60, "5 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData(3 * 60 * 60, "3 hours ago")]
    [InlineData(24 * 60 * 60, "1 day ago")]
    [InlineData(6 * 24 * 60 * 60, "6 days ago")]
    public void TheCardSaysHowLongAgoInWords(int secondsAgo, string expected) =>
        Assert.Equal(expected, RelativeTime.Describe(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void AMonthOldBoardShowsItsDate() =>
        Assert.Equal("1 Sep 2026", RelativeTime.Describe(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), Now));

    [Theory]
    [InlineData(1, "boards/seed-1.svg")]
    [InlineData(10, "boards/seed-10.svg")]
    [InlineData(0, "boards/seed-1.svg")]
    [InlineData(42, "boards/seed-10.svg")]
    public void TheCoverFollowsTheSeedWithinItsRange(int seed, string path) =>
        Assert.Equal(path, BoardImage.PathFor(seed));

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("owner", true)]
    [InlineData("Admin", true)]
    [InlineData("Member", false)]
    [InlineData(null, false)]
    public void OnlyOwnersAndAdminsMayInvite(string? role, bool allowed) =>
        Assert.Equal(allowed, Membership.CanInvite(role));

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "This board no longer exists. It may have been deleted.")]
    [InlineData(HttpStatusCode.Unauthorized, "Your session has ended. Sign in again to continue.")]
    [InlineData(HttpStatusCode.Forbidden, "This session has no active workspace, so boards cannot be changed.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many requests. Wait a moment and try again.")]
    [InlineData(HttpStatusCode.InternalServerError, "Fallback sentence.")]
    public void AFailedCallSaysWhatHappenedWithoutTheServiceBody(HttpStatusCode status, string expected) =>
        Assert.Equal(expected, BoardErrors.For(new HttpRequestException("boom", inner: null, statusCode: status), "Fallback sentence."));
}
