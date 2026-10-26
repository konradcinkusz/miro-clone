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

        return await SendAsync<BoardPageDto>(HttpMethod.Get, "api/boards" + query, body: null, sessionId, ct);
    }

    /// <summary>Creates a board in the session's workspace. The title is sent as given; the page has already checked it.</summary>
    public async Task<BoardSummaryDto> CreateAsync(string sessionId, string title, CancellationToken ct = default) =>
        await SendAsync<BoardSummaryDto>(HttpMethod.Post, "api/boards", new CreateBoardRequest(title), sessionId, ct);

    /// <summary>Renames a board. A board outside the workspace answers 404, as a board that does not exist does.</summary>
    public async Task<BoardSummaryDto> RenameAsync(string sessionId, Guid id, string title, CancellationToken ct = default) =>
        await SendAsync<BoardSummaryDto>(HttpMethod.Patch, $"api/boards/{id:D}", new RenameBoardRequest(title), sessionId, ct);

    /// <summary>Deletes a board and its favourites. The service purges the canvas with the caller's own token (ADR 0013).</summary>
    public async Task DeleteAsync(string sessionId, Guid id, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"api/boards/{id:D}", body: null, sessionId, ct);

    /// <summary>Adds or removes the board from the caller's favourites. Both calls are idempotent on the service.</summary>
    public async Task SetFavouriteAsync(string sessionId, Guid id, bool favourite, CancellationToken ct = default) =>
        await SendAsync(favourite ? HttpMethod.Put : HttpMethod.Delete, $"api/boards/{id:D}/favourite", body: null, sessionId, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string sessionId, CancellationToken ct)
    {
        using var response = await Send(method, path, body, sessionId, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new JsonException("The boards service answered with an empty body.");
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, string sessionId, CancellationToken ct)
    {
        using var response = await Send(method, path, body, sessionId, ct);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, string sessionId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Options.Set(SessionRequest.SessionId, sessionId);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException(
                $"The boards service answered {(int)response.StatusCode} for {method} {path}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        return response;
    }

    /// <summary>
    /// Finds one board of the session's workspace by walking the list, because the contract has no single-board read (ADR 0032).
    /// Returns null when the workspace does not list it. Throws <see cref="HttpRequestException"/> when a page fails.
    /// </summary>
    public async Task<BoardSummaryDto?> FindAsync(string sessionId, Guid boardId, CancellationToken ct = default)
    {
        const int pageSize = 50;
        for (var page = 1; ; page++)
        {
            var listed = await ListAsync(sessionId, search: null, favouritesOnly: false, page, pageSize, ct);
            if (listed.Items.FirstOrDefault(board => board.Id == boardId) is { } found)
            {
                return found;
            }

            if (listed.Items.Count == 0 || page * pageSize >= listed.Total)
            {
                return null;
            }
        }
    }
}
