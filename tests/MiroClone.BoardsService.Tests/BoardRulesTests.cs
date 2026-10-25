using System.Security.Claims;
using MiroClone.BoardsService.Boards;
using Xunit;

namespace MiroClone.BoardsService.Tests;

/// <summary>The rules that need no host: the title rule, paging clamps, the caller read from claims, and the search key.</summary>
public sealed class BoardRulesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Roadmap  ", "Roadmap")]
    public void TitleIsTrimmedAndRequired(string? raw, string? expected)
    {
        Assert.Equal(expected, BoardTitle.Normalize(raw));
    }

    [Fact]
    public void TitleLengthIsBoundedAtSixtyCharacters()
    {
        Assert.Equal(new string('x', 60), BoardTitle.Normalize(new string('x', 60)));
        Assert.Null(BoardTitle.Normalize(new string('x', 61)));
        Assert.Equal(new string('x', 60), BoardTitle.Normalize("   " + new string('x', 60) + "   "));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(7, 7)]
    [InlineData(50, 50)]
    [InlineData(500, 50)]
    public void PageSizeIsClampedToOneThroughFifty(int? requested, int expected)
    {
        Assert.Equal(expected, ListPaging.PageSize(requested));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(4, 4)]
    public void PageIsAtLeastOne(int? requested, int expected)
    {
        Assert.Equal(expected, ListPaging.Page(requested));
    }

    [Fact]
    public void PageIsCappedSoTheOffsetCannotOverflow()
    {
        var offset = (long)(ListPaging.Page(int.MaxValue) - 1) * ListPaging.MaxPageSize;

        Assert.Equal(ListPaging.MaxPage, ListPaging.Page(int.MaxValue));
        Assert.True(offset <= int.MaxValue);
    }

    [Fact]
    public void CallerIsReadWhenTheUserAndWorkspaceArePresent()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var principal = Principal(("sub", userId.ToString()), ("organization", workspaceId.ToString()), ("name", "  Ada  "));

        Assert.True(BoardCaller.TryRead(principal, out var caller));
        Assert.Equal(userId, caller.UserId);
        Assert.Equal(workspaceId, caller.WorkspaceId);
        Assert.Equal("Ada", caller.AuthorName);
    }

    [Fact]
    public void CallerWithoutAWorkspaceIsRefusedNotDefaulted()
    {
        var principal = Principal(("sub", Guid.NewGuid().ToString()));

        Assert.False(BoardCaller.TryRead(principal, out _));
    }

    [Fact]
    public void CallerWithoutANameFallsBackToAGenericMemberName()
    {
        var principal = Principal(("sub", Guid.NewGuid().ToString()), ("organization", Guid.NewGuid().ToString()));

        Assert.True(BoardCaller.TryRead(principal, out var caller));
        Assert.Equal("Member", caller.AuthorName);
    }

    [Fact]
    public void AnOverlongAuthorNameIsTruncatedToTheColumnWidth()
    {
        var principal = Principal(
            ("sub", Guid.NewGuid().ToString()),
            ("organization", Guid.NewGuid().ToString()),
            ("name", new string('n', 300)));

        Assert.True(BoardCaller.TryRead(principal, out var caller));
        Assert.Equal(BoardCaller.AuthorNameMaxLength, caller.AuthorName.Length);
    }

    [Fact]
    public void RenameKeepsTheSearchKeyInStepWithTheTitle()
    {
        var board = new Board();

        board.Rename("Żółw ROADMAP");

        Assert.Equal("Żółw ROADMAP", board.Title);
        Assert.Equal("żółw roadmap", board.TitleSearch);
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test"));
}
