namespace MiroClone.Seed;

/// <summary>What one run did, for the log and for the tests.</summary>
internal sealed record SeedResult(IReadOnlyDictionary<string, Guid> Organizations, int UsersRegistered, int BoardsCreated, int LayersAdded, int BoardsRemoved);

/// <summary>
/// Creates the demo estate through the services' APIs, in an order that makes a second run a no-op: sign in every user (register only when
/// sign-in is refused), per workspace find the organization by name, invite a user only when the user's token lacks it, find each board by
/// its title, add only the layers whose ids are missing. Two workspaces: a user belongs to exactly one.
/// </summary>
internal sealed class DemoSeeder(
    AuthorityClient authority,
    BoardsClient boards,
    ICanvasWriter canvas,
    IInvitationSource invitations,
    Action<string> log)
{
    private static readonly TimeSpan InvitationWait = TimeSpan.FromSeconds(30);

    // Tokens already accepted: authservice prints one per invitation, and the outbox shows them all, so the next one is the first not in here.
    private readonly HashSet<string> _usedInvitations = [];

    public async Task<SeedResult> RunAsync(bool reset, CancellationToken cancellationToken)
    {
        var organizations = new Dictionary<string, Guid>();
        int created = 0, layers = 0, removed = 0;
        foreach (var workspace in DemoData.Workspaces)
        {
            var (organization, token) = await EnsureWorkspaceAsync(workspace, cancellationToken);
            organizations[workspace.Name] = organization;
            var (c, l, r) = await SeedBoardsAsync(workspace, token, reset, cancellationToken);
            created += c;
            layers += l;
            removed += r;
        }

        return new SeedResult(organizations, authority.Registrations, created, layers, removed);
    }

    /// <summary>Creates the organization and brings every other user in with their role. Returns the organization and the founder's fresh token.</summary>
    private async Task<(Guid Organization, string FounderToken)> EnsureWorkspaceAsync(DemoWorkspace workspace, CancellationToken cancellationToken)
    {
        var tokens = new Dictionary<string, string>();
        foreach (var user in workspace.Users)
        {
            tokens[user.Email] = await authority.SignInOrRegisterAsync(user.Email, DemoData.Password, cancellationToken);
        }

        var organization = await authority.EnsureOrganizationAsync(tokens[workspace.Founder.Email], workspace.Name, cancellationToken);
        log($"organization '{workspace.Name}' is {organization}");

        foreach (var user in workspace.Users.Skip(1).Where(user => !AuthorityClient.OrganizationsOf(tokens[user.Email]).Contains(organization)))
        {
            await authority.InviteAsync(tokens[workspace.Founder.Email], organization, user.Email, user.Role, cancellationToken);
            var invitation = await WaitForInvitationAsync(user.Email, cancellationToken);
            await authority.AcceptInvitationAsync(tokens[user.Email], invitation, cancellationToken);
            _usedInvitations.Add(invitation);
            log($"{user.Email} joined as {user.Role}");
        }

        // A membership reaches a token only when the token is issued, and that includes the founder's own: everyone signs in again.
        foreach (var user in workspace.Users)
        {
            tokens[user.Email] = await authority.SignInOrRegisterAsync(user.Email, DemoData.Password, cancellationToken);
        }

        foreach (var user in workspace.Users)
        {
            if (!AuthorityClient.OrganizationsOf(tokens[user.Email]).SequenceEqual([organization]))
            {
                throw new SeedException($"{user.Email} must belong to exactly the organization {organization} ({workspace.Name}); its token says otherwise. Is the authservice database shared with other data?");
            }
        }

        return (organization, tokens[workspace.Founder.Email]);
    }

    private async Task<(int Created, int Layers, int Removed)> SeedBoardsAsync(DemoWorkspace workspace, string token, bool reset, CancellationToken cancellationToken)
    {
        var removed = 0;
        var existing = await boards.ListAsync(token, cancellationToken);
        if (reset)
        {
            foreach (var (id, title) in existing.Where(board => DemoData.IsDemoBoard(board.Title)))
            {
                await boards.DeleteAsync(token, id, cancellationToken);
                removed++;
                log($"removed demo board '{title}' of '{workspace.Name}'");
            }

            existing = [.. existing.Where(board => !DemoData.IsDemoBoard(board.Title))];
        }

        int created = 0, layers = 0;
        foreach (var demo in workspace.Boards)
        {
            var match = existing.FirstOrDefault(board => board.Title == demo.Title);
            var id = match.Id;
            if (id == Guid.Empty)
            {
                id = await boards.CreateAsync(token, demo.Title, cancellationToken);
                created++;
            }

            var added = await canvas.EnsureLayersAsync(token, id, demo.Layers, cancellationToken);
            layers += added;
            log($"board '{demo.Title}' {id}: {(match.Id == Guid.Empty ? "created" : "present")}, {added} shapes added");
        }

        return (created, layers, removed);
    }

    private async Task<string> WaitForInvitationAsync(string email, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + InvitationWait;
        do
        {
            if (await invitations.NewestAsync(cancellationToken) is { } token && !_usedInvitations.Contains(token))
            {
                return token;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new SeedException(
            $"The invitation for {email} was sent, but a new token was not found. authservice writes it to its console (\"Invitation token:\"). "
            + "Give the seed a way to read it: mount the Docker socket and set Seed__AuthServiceContainer, or set Seed__AuthServiceLogFile. "
            + "Seed__InvitationToken holds one token only, so it serves a single invitation.");
    }
}
