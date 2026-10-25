using System.Net.Http.Json;
using System.Text.Json;

namespace MiroClone.Web.Auth;

/// <summary>
/// Server-side client for authservice's HTTP surface (identity-and-accounts; ADR 0002). Browsers never call it: account
/// forms post to the BFF endpoints, which call this client. Failure messages come from authservice's own response bodies.
/// </summary>
public sealed class AuthorityClient(HttpClient http)
{
    /// <summary>Shown when authservice answers with a body this app cannot read.</summary>
    public const string UnreadableReply = "The sign-in service did not answer as expected. Try again.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Signs in with email and password. Two-factor accounts are refused: the web app does not run that step yet.</summary>
    public async Task<AuthorityResult<AuthorityTokens>> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var reply = await SendAsync(HttpMethod.Post, "api/v1/auth/login", new { email, password }, sessionId: null, ct);
        if (!reply.Succeeded)
        {
            return reply.Failure();
        }

        if (reply.Body is { } body && body.TryGetProperty("requiresTwoFactor", out var twoFactor) && twoFactor.GetBoolean())
        {
            return AuthorityResult.Failure<AuthorityTokens>("This account needs a second factor, which this app cannot take yet.", reply.Status);
        }

        return ReadTokens(reply);
    }

    /// <summary>
    /// Registers an account at the consent versions authservice currently requires. Without a verification step the reply
    /// carries tokens; with one, it says a verification email is on its way.
    /// </summary>
    public async Task<AuthorityResult<RegistrationReply>> RegisterAsync(
        string email,
        string password,
        ConsentVersions versions,
        CancellationToken ct = default)
    {
        var body = new
        {
            email,
            password,
            acceptedTermsVersion = versions.Terms,
            acceptedPrivacyVersion = versions.Privacy,
        };
        var reply = await SendAsync(HttpMethod.Post, "api/v1/auth/register", body, sessionId: null, ct);
        if (!reply.Succeeded)
        {
            return reply.Failure<RegistrationReply>();
        }

        if (reply.Body is { } json && json.TryGetProperty("accessToken", out _))
        {
            var tokens = ReadTokens(reply);
            return tokens.Succeeded
                ? AuthorityResult.Success(new RegistrationReply(VerificationRequired: false, tokens.Value))
                : AuthorityResult.Failure<RegistrationReply>(tokens.Error!, tokens.Status);
        }

        return AuthorityResult.Success(new RegistrationReply(VerificationRequired: true, Tokens: null));
    }

    /// <summary>Exchanges a refresh token for a new pair. The old refresh token is spent whatever the answer (single-use rotation).</summary>
    public async Task<AuthorityResult<AuthorityTokens>> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var reply = await SendAsync(HttpMethod.Post, "api/v1/auth/refresh", new { refreshToken }, sessionId: null, ct);
        return reply.Succeeded ? ReadTokens(reply) : reply.Failure();
    }

    /// <summary>Revokes every refresh token of the user (authservice's logout). Callers treat failure as best effort.</summary>
    public async Task<AuthorityResult<bool>> LogoutAsync(string sessionId, CancellationToken ct = default)
    {
        var reply = await SendAsync(HttpMethod.Post, "api/v1/auth/logout", body: null, sessionId, ct);
        return reply.Succeeded ? AuthorityResult.Success(true) : reply.Failure<bool>();
    }

    /// <summary>Whether the user must accept the current consent documents before using the app.</summary>
    public async Task<AuthorityResult<bool>> RequiresConsentAsync(string sessionId, CancellationToken ct = default)
    {
        var reply = await SendAsync(HttpMethod.Get, "api/v1/auth/me", body: null, sessionId, ct);
        if (!reply.Succeeded)
        {
            return reply.Failure<bool>();
        }

        return reply.Body is { } body && body.TryGetProperty("requiresConsent", out var requires) && requires.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? AuthorityResult.Success(requires.GetBoolean())
            : AuthorityResult.Failure<bool>(UnreadableReply, reply.Status);
    }

    public async Task<AuthorityResult<ConsentVersions>> GetConsentVersionsAsync(CancellationToken ct = default)
    {
        var reply = await SendAsync(HttpMethod.Get, "api/v1/auth/consents/versions", body: null, sessionId: null, ct);
        if (!reply.Succeeded || reply.Body is not { } body)
        {
            return reply.Failure<ConsentVersions>();
        }

        var terms = Text(body, "terms");
        var privacy = Text(body, "privacy");
        var cookies = Text(body, "cookies");
        return terms is null || privacy is null || cookies is null
            ? AuthorityResult.Failure<ConsentVersions>(UnreadableReply, reply.Status)
            : AuthorityResult.Success(new ConsentVersions(terms, privacy, cookies));
    }

    /// <summary>Records acceptance of the terms and the privacy policy for the signed-in user.</summary>
    public async Task<AuthorityResult<bool>> AcceptConsentAsync(string sessionId, CancellationToken ct = default)
    {
        var reply = await SendAsync(HttpMethod.Post, "api/v1/auth/consents", new { acceptedTerms = true, acceptedPrivacy = true }, sessionId, ct);
        return reply.Succeeded ? AuthorityResult.Success(true) : reply.Failure<bool>();
    }

    /// <summary>The organizations the user belongs to. The list is not a workspace choice: the token still names none (ADR 0011).</summary>
    public async Task<AuthorityResult<IReadOnlyList<AuthorityOrganization>>> ListOrganizationsAsync(string sessionId, CancellationToken ct = default)
    {
        var reply = await SendAsync(HttpMethod.Get, "api/v1/organizations", body: null, sessionId, ct);
        if (!reply.Succeeded || reply.Body is not { ValueKind: JsonValueKind.Array } body)
        {
            return reply.Failure<IReadOnlyList<AuthorityOrganization>>();
        }

        var organizations = new List<AuthorityOrganization>();
        foreach (var item in body.EnumerateArray())
        {
            var id = Text(item, "id");
            var name = Text(item, "name");
            var role = Text(item, "userRole");
            if (id is null || name is null || role is null)
            {
                return AuthorityResult.Failure<IReadOnlyList<AuthorityOrganization>>(UnreadableReply, reply.Status);
            }

            organizations.Add(new AuthorityOrganization(id, name, role));
        }

        return AuthorityResult.Success<IReadOnlyList<AuthorityOrganization>>(organizations);
    }

    /// <summary>Confirms an address with the token from the verification email.</summary>
    public Task<AuthorityResult<bool>> VerifyEmailAsync(string email, string token, CancellationToken ct = default) =>
        SendUnitAsync(HttpMethod.Post, "api/v1/auth/verify-email", new { email, token }, ct);

    /// <summary>Asks authservice to send a new verification link. It answers the same whether or not the address exists.</summary>
    public Task<AuthorityResult<bool>> ResendVerificationAsync(string email, CancellationToken ct = default) =>
        SendUnitAsync(HttpMethod.Post, "api/v1/auth/resend-verification", new { email }, ct);

    /// <summary>Asks authservice to send a reset link. It answers the same whether or not the address exists.</summary>
    public Task<AuthorityResult<bool>> ForgotPasswordAsync(string email, CancellationToken ct = default) =>
        SendUnitAsync(HttpMethod.Post, "api/v1/auth/forgot-password", new { email }, ct);

    public Task<AuthorityResult<bool>> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct = default) =>
        SendUnitAsync(HttpMethod.Post, "api/v1/auth/reset-password", new { email, token, newPassword }, ct);

    private async Task<AuthorityResult<bool>> SendUnitAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        var reply = await SendAsync(method, path, body, sessionId: null, ct);
        return reply.Succeeded ? AuthorityResult.Success(true) : reply.Failure<bool>();
    }

    /// <summary>
    /// Sends one call. A call made for a session names it on the request; <see cref="BearerTokenHandler"/> adds the token, so
    /// no caller holds one. A call without a session (sign-in, refresh) carries no bearer.
    /// </summary>
    private async Task<AuthorityReply> SendAsync(HttpMethod method, string path, object? body, string? sessionId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        if (sessionId is not null)
        {
            request.Options.Set(SessionRequest.SessionId, sessionId);
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return new AuthorityReply((int)response.StatusCode, ParseOrNull(text));
    }

    private static JsonElement? ParseOrNull(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AuthorityResult<AuthorityTokens> ReadTokens(AuthorityReply reply)
    {
        if (reply.Body is not { } body)
        {
            return AuthorityResult.Failure<AuthorityTokens>(UnreadableReply, reply.Status);
        }

        var access = Text(body, "accessToken");
        var refresh = Text(body, "refreshToken");
        if (access is null || refresh is null || !body.TryGetProperty("expiresIn", out var expiresIn) || expiresIn.ValueKind != JsonValueKind.Number)
        {
            return AuthorityResult.Failure<AuthorityTokens>(UnreadableReply, reply.Status);
        }

        return AuthorityResult.Success(new AuthorityTokens(access, refresh, expiresIn.GetInt32()));
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A registration either completes at once (tokens) or waits for the address to be verified.</summary>
    public sealed record RegistrationReply(bool VerificationRequired, AuthorityTokens? Tokens);

    private sealed record AuthorityReply(int Status, JsonElement? Body)
    {
        public bool Succeeded => Status is >= 200 and < 300;

        public AuthorityResult<T> Failure<T>() => AuthorityResult.Failure<T>(Message(), Status);

        public AuthorityResult<AuthorityTokens> Failure() => AuthorityResult.Failure<AuthorityTokens>(Message(), Status);

        /// <summary>authservice reports a problem as { error }, { message } or { errors: [...] }.</summary>
        private string Message()
        {
            if (Body is not { } body)
            {
                // A 401 with no body is the bearer handler's answer for a session that has ended.
                return Status == 401 ? AccountFlows.SessionEnded : UnreadableReply;
            }

            if (Text(body, "error") is { } error)
            {
                return error;
            }

            if (Text(body, "message") is { } message)
            {
                return message;
            }

            // Validation failures: { errors: { "Field": ["message"] } } (ProblemDetails) or { errors: ["message"] }.
            if (body.TryGetProperty("errors", out var errors))
            {
                var first = errors.ValueKind switch
                {
                    JsonValueKind.Array => errors.EnumerateArray().FirstOrDefault(),
                    JsonValueKind.Object => errors.EnumerateObject().Select(field => field.Value).FirstOrDefault(),
                    _ => default,
                };
                var text = first.ValueKind switch
                {
                    JsonValueKind.String => first.GetString(),
                    JsonValueKind.Array => first.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.String
                        ? first.EnumerateArray().First().GetString()
                        : null,
                    _ => null,
                };
                if (text is not null)
                {
                    return text;
                }
            }

            return Text(body, "title") ?? UnreadableReply;
        }
    }
}
