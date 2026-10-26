using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiroClone.Web.E2E.Stack;

/// <summary>An account created for one journey. The password is a throwaway development value.</summary>
public sealed record Account(string Email, string Password)
{
    public static Account New(string prefix) =>
        new($"{prefix}-{Guid.NewGuid():N}@dev.test", $"E2E-{Guid.NewGuid():N}!"); // gitleaks:allow
}

/// <summary>
/// The account steps the journeys need before a browser signs in: registration, organizations and invitations, through
/// authservice's own HTTP API (the same calls scripts/seed-dev-users.sh makes). Sign-in itself is done in the browser.
/// </summary>
public sealed class AuthorityApi(HttpClient http, EstateFixture stack)
{
    private static readonly Regex InvitationToken = new("Invitation token: ([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);

    /// <summary>Registers an account and returns its access token (the dev outbox issues no verification step by default).</summary>
    public async Task<string> RegisterAsync(Account account)
    {
        var versions = await GetJsonAsync("api/v1/auth/consents/versions");
        var body = new
        {
            email = account.Email,
            password = account.Password,
            acceptedTermsVersion = versions.GetProperty("terms").GetString(),
            acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
        };
        using var reply = await http.PostAsJsonAsync(Url("api/v1/auth/register"), body);
        await EnsureSuccessAsync(reply, "register");
        return TokenOf(await reply.Content.ReadFromJsonAsync<JsonElement>());
    }

    public async Task<string> SignInAsync(Account account)
    {
        using var reply = await http.PostAsJsonAsync(Url("api/v1/auth/login"), new { email = account.Email, password = account.Password });
        await EnsureSuccessAsync(reply, "sign in");
        return TokenOf(await reply.Content.ReadFromJsonAsync<JsonElement>());
    }

    public async Task<Guid> CreateOrganizationAsync(string token, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("api/v1/organizations"))
        {
            Content = JsonContent.Create(new { name }),
        };
        request.Headers.Authorization = new("Bearer", token);
        using var reply = await http.SendAsync(request);
        await EnsureSuccessAsync(reply, "create organization");
        var created = await reply.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(created.GetProperty("id").GetString()!);
    }

    /// <summary>Invites a member and returns the invitation token, which only the dev outbox log carries.</summary>
    public async Task<string> InviteAsync(string ownerToken, Guid organization, string email)
    {
        var before = await DevOutbox.InvitationCountAsync(stack);
        using var request = new HttpRequestMessage(HttpMethod.Post, Url($"api/v1/organizations/{organization:D}/invite"))
        {
            Content = JsonContent.Create(new { email, role = "Member" }),
        };
        request.Headers.Authorization = new("Bearer", ownerToken);
        using var reply = await http.SendAsync(request);
        await EnsureSuccessAsync(reply, "invite");
        return await DevOutbox.NewestInvitationAsync(stack, before);
    }

    public async Task AcceptInvitationAsync(string memberToken, string invitation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("api/v1/organizations/invitations/accept"))
        {
            Content = JsonContent.Create(new { token = invitation }),
        };
        request.Headers.Authorization = new("Bearer", memberToken);
        using var reply = await http.SendAsync(request);
        await EnsureSuccessAsync(reply, "accept invitation");
    }

    /// <summary>An owner with one organization: the single-workspace case the boards service accepts (ADR 0024).</summary>
    public async Task<(Account Account, Guid Organization)> CreateWorkspaceOwnerAsync(string organizationName)
    {
        var owner = Account.New("owner");
        await RegisterAsync(owner);
        var token = await SignInAsync(owner);
        var organization = await CreateOrganizationAsync(token, organizationName);
        return (owner, organization);
    }

    /// <summary>Registers a member and invites it into the organization, through the dev outbox.</summary>
    public async Task<Account> AddMemberAsync(Account owner, Guid organization)
    {
        var member = Account.New("member");
        var memberToken = await RegisterAsync(member);
        var ownerToken = await SignInAsync(owner);
        var invitation = await InviteAsync(ownerToken, organization, member.Email);
        await AcceptInvitationAsync(memberToken, invitation);
        return member;
    }

    private static string TokenOf(JsonElement reply) => reply.GetProperty("accessToken").GetString()!;

    private string Url(string path) => $"{stack.AuthServiceUrl}/{path}";

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var reply = await http.GetAsync(Url(path));
        await EnsureSuccessAsync(reply, path);
        return await reply.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage reply, string step)
    {
        if (!reply.IsSuccessStatusCode)
        {
            var body = await reply.Content.ReadAsStringAsync();
            throw new HttpRequestException($"authservice refused {step}: {(int)reply.StatusCode} {body}", null, reply.StatusCode);
        }
    }

    /// <summary>The newest invitation token in the outbox log, for the caller to read after its own invitation was sent.</summary>
    internal static IReadOnlyList<string> InvitationTokens(string log) =>
        InvitationToken.Matches(log).Select(match => match.Groups[1].Value).ToArray();
}
