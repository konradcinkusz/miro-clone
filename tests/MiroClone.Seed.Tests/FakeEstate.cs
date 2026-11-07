using System.Net;
using System.Text;
using System.Text.Json;
using MiroClone.Seed;

namespace MiroClone.Seed.Tests;

/// <summary>An in-memory authservice and boards service behind one HttpMessageHandler, with the behaviour the seed relies on.</summary>
internal sealed class FakeEstate : HttpMessageHandler
{
    public const string Terms = "2026-01-01";

    private readonly Dictionary<string, (Guid Id, string Password)> _users = [];
    private readonly Dictionary<string, Guid> _organizations = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _memberships = [];
    private readonly Dictionary<Guid, (Guid Workspace, string Title)> _boards = [];
    private readonly Dictionary<string, (Guid Organization, string Role)> _invitedEmails = [];
    private readonly Dictionary<string, (Guid User, Guid Organization, string Role)> _pending = [];
    private readonly Dictionary<(Guid User, Guid Organization), string> _roles = [];

    public int Registrations { get; private set; }

    public int InvitesSent { get; private set; }

    public IReadOnlyDictionary<Guid, (Guid Workspace, string Title)> Boards => _boards;

    public IReadOnlyDictionary<string, Guid> Organizations => _organizations;

    public IReadOnlyDictionary<(Guid User, Guid Organization), string> Roles => _roles;

    public Guid UserId(string email) => _users[email].Id;

    public string LastInvitationToken { get; private set; } = string.Empty;

    public AuthorityClient Authority => new(new HttpClient(this) { BaseAddress = new Uri("http://auth/") });

    public BoardsClient BoardsApi => new(new HttpClient(this) { BaseAddress = new Uri("http://boards/") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? default : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement;
        var path = request.RequestUri!.AbsolutePath;
        var caller = Caller(request);
        return (request.Method.Method, path) switch
        {
            ("GET", "/api/v1/auth/consents/versions") => Json(new { terms = Terms, privacy = Terms }),
            ("POST", "/api/v1/auth/register") => Register(body),
            ("POST", "/api/v1/auth/login") => Login(body),
            ("GET", "/api/v1/organizations") => ListOrganizations(caller),
            ("POST", "/api/v1/organizations") => CreateOrganization(caller, body),
            ("POST", "/api/v1/organizations/invitations/accept") => Accept(caller, body),
            ("GET", "/api/boards") => ListBoards(caller),
            ("POST", "/api/boards") => CreateBoard(caller, body),
            _ when path.EndsWith("/invite", StringComparison.Ordinal) => Invite(path, body),
            _ when request.Method == HttpMethod.Delete && path.StartsWith("/api/boards/", StringComparison.Ordinal) => DeleteBoard(caller, path),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage Register(JsonElement body)
    {
        var email = body.GetProperty("email").GetString()!;
        if (body.GetProperty("acceptedTermsVersion").GetString() != Terms || _users.ContainsKey(email))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        _users[email] = (Guid.NewGuid(), body.GetProperty("password").GetString()!);
        Registrations++;
        return Json(new { accessToken = TokenFor(_users[email].Id) });
    }

    private HttpResponseMessage Login(JsonElement body)
    {
        var email = body.GetProperty("email").GetString()!;
        return _users.TryGetValue(email, out var user) && user.Password == body.GetProperty("password").GetString()
            ? Json(new { accessToken = TokenFor(user.Id) })
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);
    }

    private HttpResponseMessage ListOrganizations(Guid caller) =>
        Json(_organizations.Where(pair => _memberships.TryGetValue(caller, out var set) && set.Contains(pair.Value))
            .Select(pair => new { id = pair.Value.ToString("D"), name = pair.Key }));

    private HttpResponseMessage CreateOrganization(Guid caller, JsonElement body)
    {
        var id = Guid.NewGuid();
        _organizations[body.GetProperty("name").GetString()!] = id;
        Join(caller, id, "Owner");
        return Json(new { id = id.ToString("D") });
    }

    private HttpResponseMessage Invite(string path, JsonElement body)
    {
        var email = body.GetProperty("email").GetString()!;
        var organization = Guid.Parse(path.Split('/')[^2]);
        if (_invitedEmails.ContainsKey(email))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        var role = body.GetProperty("role").GetString()!;
        _invitedEmails[email] = (organization, role);
        InvitesSent++;
        LastInvitationToken = $"inv-{Guid.NewGuid():N}";
        _pending[LastInvitationToken] = (_users[email].Id, organization, role);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private HttpResponseMessage Accept(Guid caller, JsonElement body)
    {
        if (!_pending.Remove(body.GetProperty("token").GetString()!, out var invitation) || invitation.User != caller)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        Join(caller, invitation.Organization, invitation.Role);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private HttpResponseMessage ListBoards(Guid caller)
    {
        var workspace = WorkspaceOf(caller);
        var items = _boards.Where(pair => pair.Value.Workspace == workspace)
            .Select(pair => new { id = pair.Key.ToString("D"), title = pair.Value.Title }).ToArray();
        return Json(new { items, total = items.Length });
    }

    private HttpResponseMessage CreateBoard(Guid caller, JsonElement body)
    {
        var id = Guid.NewGuid();
        _boards[id] = (WorkspaceOf(caller), body.GetProperty("title").GetString()!);
        return Json(new { id = id.ToString("D") }, HttpStatusCode.Created);
    }

    private HttpResponseMessage DeleteBoard(Guid caller, string path)
    {
        var id = Guid.Parse(path["/api/boards/".Length..]);
        return _boards.TryGetValue(id, out var board) && board.Workspace == WorkspaceOf(caller) && _boards.Remove(id)
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>Adds a board of another workspace, which a reset must never touch.</summary>
    public Guid AddForeignBoard(string title)
    {
        var id = Guid.NewGuid();
        _boards[id] = (Guid.NewGuid(), title);
        return id;
    }

    /// <summary>Adds a board of the demo workspace that is not a demo board.</summary>
    public Guid AddRealBoardInWorkspace(string title)
    {
        var id = Guid.NewGuid();
        _boards[id] = (_organizations[DemoData.OrganizationName], title);
        return id;
    }

    private void Join(Guid user, Guid organization, string role)
    {
        _roles[(user, organization)] = role;
        if (!_memberships.TryGetValue(user, out var set))
        {
            _memberships[user] = set = [];
        }

        set.Add(organization);
    }

    private Guid WorkspaceOf(Guid user) => _memberships[user].Single();

    private string TokenFor(Guid user)
    {
        var organizations = _memberships.TryGetValue(user, out var set) ? set.Select(id => id.ToString("D")).ToArray() : [];
        var claims = JsonSerializer.Serialize(new { sub = user.ToString("D"), organization = organizations });
        return $"e30.{Base64Url(claims)}.sig";
    }

    private static Guid Caller(HttpRequestMessage request)
    {
        var token = request.Headers.Authorization?.Parameter;
        if (token is null)
        {
            return Guid.Empty;
        }

        var payload = Convert.FromBase64String(token.Split('.')[1].Replace('-', '+').Replace('_', '/').PadRight(((token.Split('.')[1].Length + 3) / 4) * 4, '='));
        return Guid.Parse(JsonDocument.Parse(payload).RootElement.GetProperty("sub").GetString()!);
    }

    private static string Base64Url(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}

/// <summary>A canvas that keeps layers by board in memory and counts the writes it accepted.</summary>
internal sealed class FakeCanvas : ICanvasWriter
{
    public Dictionary<Guid, HashSet<Guid>> Layers { get; } = [];

    public int Writes { get; private set; }

    public Task<int> EnsureLayersAsync(string token, Guid board, IReadOnlyList<DemoLayer> layers, CancellationToken cancellationToken)
    {
        if (!Layers.TryGetValue(board, out var present))
        {
            Layers[board] = present = [];
        }

        var added = layers.Count(layer => present.Add(layer.Id));
        Writes += added;
        return Task.FromResult(added);
    }
}

internal sealed class FakeInvitations(FakeEstate estate) : IInvitationSource
{
    public Task<string?> NewestAsync(CancellationToken cancellationToken) =>
        Task.FromResult(estate.LastInvitationToken.Length == 0 ? null : estate.LastInvitationToken);
}
