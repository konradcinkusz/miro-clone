using Xunit;

namespace MiroClone.Seed.Tests;

public sealed class DemoSeederTests
{
    private static DemoSeeder SeederFor(FakeEstate estate, FakeCanvas canvas) =>
        new(estate.Authority, estate.BoardsApi, canvas, new FakeInvitations(estate), _ => { });

    [Fact]
    public async Task TheFirstRunCreatesFourUsersInTwoOrganizationsAndTheDemoBoardsWithShapes()
    {
        var estate = new FakeEstate();
        var canvas = new FakeCanvas();

        var result = await SeederFor(estate, canvas).RunAsync(false, CancellationToken.None);

        Assert.Equal(4, estate.Registrations);
        Assert.Equal([DemoData.OrganizationName, DemoData.OtherOrganizationName], estate.Organizations.Keys.Order());
        var expectedTitles = DemoData.Workspaces.SelectMany(w => w.Boards).Select(b => b.Title).Order();
        Assert.Equal(expectedTitles, estate.Boards.Values.Select(b => b.Title).Order());
        Assert.Equal(DemoData.Workspaces.Sum(w => w.Boards.Count), result.BoardsCreated);
        Assert.Equal(DemoData.Workspaces.Sum(w => w.Boards.Sum(b => b.Layers.Count)), result.LayersAdded);
        Assert.Equal(2, estate.InvitesSent);
    }

    [Fact]
    public async Task EachUserHasTheirRoleAndBelongsToExactlyOneOrganization()
    {
        var estate = new FakeEstate();
        await SeederFor(estate, new FakeCanvas()).RunAsync(false, CancellationToken.None);
        var dev = estate.Organizations[DemoData.OrganizationName];
        var other = estate.Organizations[DemoData.OtherOrganizationName];

        Assert.Equal("Owner", estate.Roles[(estate.UserId(DemoData.OwnerEmail), dev)]);
        Assert.Equal("Admin", estate.Roles[(estate.UserId(DemoData.AdminEmail), dev)]);
        Assert.Equal("Member", estate.Roles[(estate.UserId(DemoData.MemberEmail), dev)]);
        Assert.Equal("Owner", estate.Roles[(estate.UserId(DemoData.OtherEmail), other)]);
        Assert.Equal(4, estate.Roles.Count);
        Assert.DoesNotContain(estate.Roles.Keys, key => key.User == estate.UserId(DemoData.OtherEmail) && key.Organization == dev);
    }

    [Fact]
    public async Task EachWorkspaceSeesOnlyItsOwnBoards()
    {
        var estate = new FakeEstate();
        await SeederFor(estate, new FakeCanvas()).RunAsync(false, CancellationToken.None);

        var other = estate.Organizations[DemoData.OtherOrganizationName];
        var dev = estate.Organizations[DemoData.OrganizationName];
        Assert.Equal([DemoData.OtherBoardTitle], estate.Boards.Values.Where(b => b.Workspace == other).Select(b => b.Title));
        Assert.Equal(DemoData.Boards.Count, estate.Boards.Values.Count(b => b.Workspace == dev));
        Assert.DoesNotContain(estate.Boards.Values.Where(b => b.Workspace == dev), b => b.Title == DemoData.OtherBoardTitle);
    }

    [Fact]
    public async Task ARepeatRunChangesNothing()
    {
        var estate = new FakeEstate();
        var canvas = new FakeCanvas();
        await SeederFor(estate, canvas).RunAsync(false, CancellationToken.None);
        var boards = estate.Boards.ToDictionary(pair => pair.Key, pair => pair.Value);
        var writes = canvas.Writes;

        var second = await SeederFor(estate, canvas).RunAsync(false, CancellationToken.None);

        Assert.Equal(0, second.UsersRegistered);
        Assert.Equal(0, second.BoardsCreated);
        Assert.Equal(0, second.LayersAdded);
        Assert.Equal(2, estate.InvitesSent);
        Assert.Equal(writes, canvas.Writes);
        Assert.Equal(boards, estate.Boards.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    [Fact]
    public async Task ResetRemovesOnlyDemoBoardsAndThenSeedsThemAgain()
    {
        var estate = new FakeEstate();
        var canvas = new FakeCanvas();
        await SeederFor(estate, canvas).RunAsync(false, CancellationToken.None);
        var real = estate.AddRealBoardInWorkspace("Quarterly roadmap");
        var foreign = estate.AddForeignBoard($"{DemoData.BoardPrefix}someone else's");

        var result = await SeederFor(estate, canvas).RunAsync(true, CancellationToken.None);

        Assert.Equal(DemoData.Workspaces.Sum(w => w.Boards.Count), result.BoardsRemoved);
        Assert.Contains(real, estate.Boards.Keys);
        Assert.Contains(foreign, estate.Boards.Keys);
        Assert.Equal(DemoData.Workspaces.Sum(w => w.Boards.Count) + 2, estate.Boards.Count);
    }

    [Fact]
    public void LayerIdsAreStableAndDistinct()
    {
        var all = DemoData.Workspaces.SelectMany(w => w.Boards).SelectMany(board => board.Layers).Select(layer => layer.Id).ToArray();

        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Equal(DemoData.LayerId("welcome/frame"), DemoData.LayerId("welcome/frame"));
    }

    [Fact]
    public void ThePasswordSatisfiesTheStrictRuleOfAuthservice()
    {
        // authservice asks for a mix of upper case, lower case, a digit and a symbol, 12 characters or more.
        Assert.True(DemoData.Password.Length >= 12);
        Assert.Contains(DemoData.Password, char.IsUpper);
        Assert.Contains(DemoData.Password, char.IsLower);
        Assert.Contains(DemoData.Password, char.IsDigit);
        Assert.Contains(DemoData.Password, c => !char.IsLetterOrDigit(c));
    }
}
