using System.Collections;
using MiroClone.Seed;

// The demo seed (ADR 0050). Exit codes: 0 done or disabled, 1 refused or failed. Run by the AppHost (a project resource) and by
// compose.yaml (a one-shot container), after authservice, boards-service and canvas-service answer their health probes.
try
{
    var variables = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => entry.Value as string);
    var settings = SeedSettings.From(variables, args);
    if (!settings.Enabled)
    {
        Console.WriteLine("seed: disabled (Seed__Enabled=false), nothing to do.");
        return 0;
    }

    using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    using var authorityHttp = new HttpClient { BaseAddress = settings.AuthServiceUrl };
    using var boardsHttp = new HttpClient { BaseAddress = settings.BoardsUrl };
    using var canvasHttp = new HttpClient();
    var seeder = new DemoSeeder(
        new AuthorityClient(authorityHttp),
        new BoardsClient(boardsHttp),
        new CanvasWriter(canvasHttp, settings.CanvasUrl),
        new ConfiguredInvitationSource(settings),
        message => Console.WriteLine($"seed: {message}"));

    var result = await seeder.RunAsync(settings.Reset, cancellation.Token);
    Console.WriteLine(
        $"seed: done. organization {result.Organization}; registered {result.UsersRegistered}, boards created {result.BoardsCreated}, shapes added {result.LayersAdded}, boards removed {result.BoardsRemoved}.");
    Console.WriteLine();
    Console.WriteLine("seed: ============================ DEMO LOGINS (development only) ============================");
    Console.WriteLine($"seed:   {"owner",-8} {DemoData.OwnerEmail,-18} {DemoData.Password}   owner of '{DemoData.OrganizationName}'");
    Console.WriteLine($"seed:   {"member",-8} {DemoData.MemberEmail,-18} {DemoData.Password}   member of '{DemoData.OrganizationName}'");
    Console.WriteLine($"seed:   boards: {string.Join(", ", DemoData.Boards.Select(board => board.Title))}");
    Console.WriteLine("seed:   sign in as one in each of two browsers to edit a board together live.");
    Console.WriteLine("seed: ========================================================================================");
    return 0;
}
catch (SeedException error)
{
    Console.Error.WriteLine($"seed: {error.Message}");
    return 1;
}
catch (Exception error) when (error is HttpRequestException or TaskCanceledException or Microsoft.AspNetCore.SignalR.HubException)
{
    Console.Error.WriteLine($"seed: failed: {error.Message}");
    return 1;
}
