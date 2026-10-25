using System.Net.Http.Json;
using System.Text.Json;
using MiroClone.Web.Auth;

namespace MiroClone.Web.Api;

/// <summary>
/// The circuit's client for the canvas service (/api/canvas). Same rules as <see cref="BoardsClient"/>: the session names the
/// caller, the bearer token is added by the handler, and the service defaults supply the one resilience handler.
/// </summary>
public sealed class CanvasClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The board's canvas snapshot. Throws <see cref="HttpRequestException"/> when the service refuses or fails.</summary>
    public async Task<CanvasStateDto> GetAsync(string sessionId, Guid boardId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/canvas/{boardId:D}");
        request.Options.Set(SessionRequest.SessionId, sessionId);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CanvasStateDto>(Json, ct)
            ?? throw new JsonException("The canvas service answered with an empty state.");
    }
}
