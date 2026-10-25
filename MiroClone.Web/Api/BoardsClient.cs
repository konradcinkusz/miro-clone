using System.Net.Http.Json;
using System.Text.Json;
using MiroClone.Web.Auth;

namespace MiroClone.Web.Api;

/// <summary>
/// The circuit's client for the boards service (/api/boards). Every call names the session it is for; the bearer token is
/// added by <see cref="BearerTokenHandler"/>, so no page or component ever holds a token. One resilience handler is applied by
/// the service defaults (ADR 0005), so this client adds none.
/// </summary>
public sealed class BoardsClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lists boards in the session's workspace. Throws <see cref="HttpRequestException"/> when the service refuses or fails.</summary>
    public async Task<BoardPageDto> ListAsync(string sessionId, string? search, bool favouritesOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var query = $"?page={page}&pageSize={pageSize}"
            + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}")
            + (favouritesOnly ? "&favourites=true" : string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/boards" + query);
        request.Options.Set(SessionRequest.SessionId, sessionId);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BoardPageDto>(Json, ct)
            ?? throw new JsonException("The boards service answered with an empty page.");
    }
}
