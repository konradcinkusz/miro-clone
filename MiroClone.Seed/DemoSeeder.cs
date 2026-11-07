namespace MiroClone.Seed;

/// <summary>What one run did, for the log and for the tests.</summary>
internal sealed record SeedResult(Guid Organization, int UsersRegistered, int BoardsCreated, int LayersAdded, int BoardsRemoved);

/// <summary>
/// Creates the demo estate through the services' APIs, in an order that makes a second run a no-op: sign in (register only when
/// sign-in is refused), find the organization by name, bring the member in only when the member's token lacks it, find each board by
/// its title, add only the layers whose ids are missing.
/// </summary>
internal sealed class DemoSeeder(
    AuthorityClient authority,
    BoardsClient boards,
    ICanvasWriter canvas,
    IInvitationSource invitations,
    Action<string> log)
{
    private static readonly TimeSpan InvitationWait = TimeSpan.FromSeconds(30);

    public async Task<SeedResult> RunAsync(bool reset, CancellationToken cancellationToken)
    {
        var owner = await authority.SignInOrRegisterAsync(DemoData.OwnerEmail, DemoData.Password, cancellationToken);
        var member = await authority.SignInOrRegisterAsync(DemoData.MemberEmail, DemoData.Password, cancellationToken);

        var organization = await authority.EnsureOrganizationAsync(owner, DemoData.OrganizationName, cancellationToken);
        log($"organization '{DemoData.OrganizationName}' is {organization}");

        if (!AuthorityClient.OrganizationsOf(member).Contains(organization))
        {
            await authority.InviteAsync(owner, organization, DemoData.MemberEmail, cancellationToken);
            var invitation = await WaitForInvitationAsync(cancellationToken);
            await authority.AcceptInvitationAsync(member, invitation, cancellationToken);
            log($"{DemoData.MemberEmail} joined the organization");
        }

        // A membership reaches a token only when the token is issued, so both sign in again.
        owner = await authority.SignInOrRegisterAsync(DemoData.OwnerEmail, DemoData.Password, cancellationToken);
        member = await authority.SignInOrRegisterAsync(DemoData.MemberEmail, DemoData.Password, cancellationToken);
        if (!AuthorityClient.OrganizationsOf(member).SequenceEqual([organization]) || !AuthorityClient.OrganizationsOf(owner).SequenceEqual([organization]))
        {
            throw new SeedException($"The demo users must belong to exactly the organization {organization}; their tokens say otherwise. Is the authservice database shared with other data?");
        }

        var removed = 0;
        var existing = await boards.ListAsync(owner, cancellationToken);
        if (reset)
        {
            foreach (var (id, title) in existing.Where(board => DemoData.IsDemoBoard(board.Title)))
            {
                await boards.DeleteAsync(owner, id, cancellationToken);
                removed++;
                log($"removed demo board '{title}'");
            }

            existing = [.. existing.Where(board => !DemoData.IsDemoBoard(board.Title))];
        }

        int created = 0, layers = 0;
        foreach (var demo in DemoData.Boards)
        {
            var match = existing.FirstOrDefault(board => board.Title == demo.Title);
            var id = match.Id;
            if (id == Guid.Empty)
            {
                id = await boards.CreateAsync(owner, demo.Title, cancellationToken);
                created++;
            }

            var added = await canvas.EnsureLayersAsync(owner, id, demo.Layers, cancellationToken);
            layers += added;
            log($"board '{demo.Title}' {id}: {(match.Id == Guid.Empty ? "created" : "present")}, {added} shapes added");
        }

        return new SeedResult(organization, authority.Registrations, created, layers, removed);
    }

    private async Task<string> WaitForInvitationAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + InvitationWait;
        do
        {
            if (await invitations.NewestAsync(cancellationToken) is { } token)
            {
                return token;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new SeedException(
            $"The invitation for {DemoData.MemberEmail} was sent, but its token was not found. authservice writes it to its console (\"Invitation token:\"). "
            + "Give the seed a way to read it: mount the Docker socket and set Seed__AuthServiceContainer, or set Seed__AuthServiceLogFile or Seed__InvitationToken.");
    }
}
