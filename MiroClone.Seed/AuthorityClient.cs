using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MiroClone.Seed;

/// <summary>The authservice calls the seed needs (docs/AUTH.md): register, sign in, organizations, invitations.</summary>
internal sealed class AuthorityClient(HttpClient http)
{
    /// <summary>How many accounts this run registered. Zero on a repeat run.</summary>
    public int Registrations { get; private set; }

    /// <summary>Signs in, and registers the account first when sign-in is refused.</summary>
    public async Task<string> SignInOrRegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        using (var login = await http.PostAsJsonAsync("api/v1/auth/login", new { email, password }, cancellationToken))
        {
            if (login.IsSuccessStatusCode)
            {
                return await AccessTokenAsync(login, cancellationToken);
            }
        }

        using var versionsReply = await http.GetAsync("api/v1/auth/consents/versions", cancellationToken);
        await EnsureAsync(versionsReply, "read consent versions");
        var versions = await versionsReply.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var register = await http.PostAsJsonAsync(
            "api/v1/auth/register",
            new
            {
                email,
                password,
                acceptedTermsVersion = versions.GetProperty("terms").GetString(),
                acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
            },
            cancellationToken);
        await EnsureAsync(register, $"register {email}");
        Registrations++;
        return await AccessTokenAsync(register, cancellationToken);
    }

    public async Task<Guid> EnsureOrganizationAsync(string ownerToken, string name, CancellationToken cancellationToken)
    {
        using var list = await SendAsync(HttpMethod.Get, "api/v1/organizations", ownerToken, null, cancellationToken);
        await EnsureAsync(list, "list organizations");
        foreach (var organization in (await list.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray())
        {
            if (organization.GetProperty("name").GetString() == name)
            {
                return Guid.Parse(organization.GetProperty("id").GetString()!);
            }
        }

        using var create = await SendAsync(HttpMethod.Post, "api/v1/organizations", ownerToken, new { name }, cancellationToken);
        await EnsureAsync(create, "create the organization");
        return Guid.Parse((await create.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetString()!);
    }

    /// <summary>Invites a user with a role (Admin or Member). A repeat answers 400 ("already sent") and the earlier invitation still works, so that is not a failure.</summary>
    public async Task InviteAsync(string ownerToken, Guid organization, string email, string role, CancellationToken cancellationToken)
    {
        using var reply = await SendAsync(HttpMethod.Post, $"api/v1/organizations/{organization:D}/invite", ownerToken, new { email, role }, cancellationToken);
        if (!reply.IsSuccessStatusCode && (int)reply.StatusCode != 400)
        {
            await EnsureAsync(reply, "invite the member");
        }
    }

    public async Task AcceptInvitationAsync(string memberToken, string invitation, CancellationToken cancellationToken)
    {
        using var reply = await SendAsync(HttpMethod.Post, "api/v1/organizations/invitations/accept", memberToken, new { token = invitation }, cancellationToken);
        await EnsureAsync(reply, "accept the invitation");
    }

    /// <summary>The organization ids in a token. A membership reaches a token only when it is issued, so a new member signs in again.</summary>
    public static IReadOnlyList<Guid> OrganizationsOf(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
        if (!claims.TryGetProperty("organization", out var value))
        {
            return [];
        }

        return value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(item => Guid.Parse(item.GetString()!))]
            : [Guid.Parse(value.GetString()!)];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task<string> AccessTokenAsync(HttpResponseMessage reply, CancellationToken cancellationToken) =>
        (await reply.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("accessToken").GetString()!;

    internal static async Task EnsureAsync(HttpResponseMessage reply, string step)
    {
        if (!reply.IsSuccessStatusCode)
        {
            throw new SeedException($"authservice refused to {step}: {(int)reply.StatusCode} {await reply.Content.ReadAsStringAsync()}");
        }
    }
}
