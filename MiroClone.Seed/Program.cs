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
        $"seed: done. organizations {string.Join(", ", result.Organizations.Select(pair => $"'{pair.Key}' {pair.Value}"))}; registered {result.UsersRegistered}, boards created {result.BoardsCreated}, shapes added {result.LayersAdded}, boards removed {result.BoardsRemoved}.");
    Console.WriteLine();
    Console.WriteLine("seed: ============================ DEMO LOGINS (development only) ============================");
    foreach (var workspace in DemoData.Workspaces)
    {
        foreach (var user in workspace.Users)
        {
            Console.WriteLine($"seed:   {user.Role,-7} {user.Email,-18} {DemoData.Password}   {user.Role.ToLowerInvariant()} of '{workspace.Name}'");
        }
    }

    Console.WriteLine($"seed:   boards: {string.Join(", ", DemoData.Workspaces.SelectMany(workspace => workspace.Boards).Select(board => board.Title))}");
    Console.WriteLine("seed:   sign in as two users in two browsers to edit a board together live; the Other Workspace user cannot see the Dev Workspace boards.");
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
