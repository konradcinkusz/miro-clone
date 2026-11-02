using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace MiroClone.Seed;

/// <summary>Puts shapes on a board.</summary>
internal interface ICanvasWriter
{
    /// <summary>Makes sure each layer is on the board. A layer that is already there is left alone. Returns how many were added.</summary>
    Task<int> EnsureLayersAsync(string token, Guid board, IReadOnlyList<DemoLayer> layers, CancellationToken cancellationToken);
}

/// <summary>
/// Writes through CanvasService's public surface: REST to read what is there, the SignalR hub (join, then upsert mutations) to add what
/// is missing. An upsert of an existing id changes nothing (the aggregate is idempotent), so a second run adds nothing.
/// </summary>
internal sealed class CanvasWriter(HttpClient http, Uri canvasUrl) : ICanvasWriter
{
    public async Task<int> EnsureLayersAsync(string token, Guid board, IReadOnlyList<DemoLayer> layers, CancellationToken cancellationToken)
    {
        if (layers.Count == 0)
        {
            return 0;
        }

        var present = await PresentLayersAsync(token, board, cancellationToken);
        var missing = layers.Where(layer => !present.Contains(layer.Id)).ToArray();
        if (missing.Length == 0)
        {
            return 0;
        }

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(canvasUrl, "hubs/canvas"), options => options.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .Build();
        await connection.StartAsync(cancellationToken);
        await connection.InvokeAsync("JoinBoard", board, cancellationToken);
        foreach (var layer in missing)
        {
            await connection.InvokeAsync("SendMutation", board, new { kind = "Upsert", layer }, cancellationToken);
        }

        // The hub applies a mutation before it answers, so by now the canvas holds every layer; the snapshot to storage is write-behind.
        await connection.StopAsync(cancellationToken);
        return missing.Length;
    }

    private async Task<HashSet<Guid>> PresentLayersAsync(string token, Guid board, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(canvasUrl, $"api/canvas/{board:D}"));
        request.Headers.Authorization = new("Bearer", token);
        using var reply = await http.SendAsync(request, cancellationToken);
        await AuthorityClient.EnsureAsync(reply, "read the canvas");
        var state = await reply.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return [.. state.GetProperty("layerIds").EnumerateArray().Select(id => Guid.Parse(id.GetString()!))];
    }
}
