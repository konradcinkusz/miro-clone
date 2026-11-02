using Xunit;

namespace MiroClone.Seed.Tests;

public sealed class DemoSeederTests
{
    private static DemoSeeder SeederFor(FakeEstate estate, FakeCanvas canvas) =>
        new(estate.Authority, estate.BoardsApi, canvas, new FakeInvitations(estate), _ => { });

    [Fact]
    public async Task TheFirstRunCreatesTwoUsersInOneOrganizationAndTheDemoBoardsWithShapes()
    {
        var estate = new FakeEstate();
        var canvas = new FakeCanvas();

        var result = await SeederFor(estate, canvas).RunAsync(false, CancellationToken.None);

        Assert.Equal(2, estate.Registrations);
        Assert.Single(estate.Organizations);
        Assert.Equal(DemoData.OrganizationName, estate.Organizations.Keys.Single());
        Assert.Equal(DemoData.Boards.Select(b => b.Title).Order(), estate.Boards.Values.Select(b => b.Title).Order());
        Assert.Equal(DemoData.Boards.Count, result.BoardsCreated);
        Assert.Equal(DemoData.Boards.Sum(b => b.Layers.Count), result.LayersAdded);
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
        Assert.Equal(1, estate.InvitesSent);
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

        Assert.Equal(DemoData.Boards.Count, result.BoardsRemoved);
        Assert.Contains(real, estate.Boards.Keys);
        Assert.Contains(foreign, estate.Boards.Keys);
        Assert.Equal(DemoData.Boards.Count + 2, estate.Boards.Count);
    }

    [Fact]
    public void LayerIdsAreStableAndDistinct()
    {
        var all = DemoData.Boards.SelectMany(board => board.Layers).Select(layer => layer.Id).ToArray();

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
