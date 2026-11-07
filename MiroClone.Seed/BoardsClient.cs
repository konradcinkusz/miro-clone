using System.Net.Http.Json;
using System.Text.Json;

namespace MiroClone.Seed;

/// <summary>The boards calls the seed needs (frozen contract: GET, POST and DELETE on /api/boards).</summary>
internal sealed class BoardsClient(HttpClient http)
{
    private const int PageSize = 50;

    /// <summary>Every board the caller's workspace holds, page by page.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Title)>> ListAsync(string token, CancellationToken cancellationToken)
    {
        var all = new List<(Guid, string)>();
        for (var page = 1; ; page++)
        {
            using var request = Request(HttpMethod.Get, $"api/boards?page={page}&pageSize={PageSize}", token);
            using var reply = await http.SendAsync(request, cancellationToken);
            await AuthorityClient.EnsureAsync(reply, "list boards");
            var body = await reply.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var items = body.GetProperty("items").EnumerateArray().ToArray();
            all.AddRange(items.Select(item => (Guid.Parse(item.GetProperty("id").GetString()!), item.GetProperty("title").GetString()!)));
            if (items.Length < PageSize || all.Count >= body.GetProperty("total").GetInt32())
            {
                return all;
            }
        }
    }

    public async Task<Guid> CreateAsync(string token, string title, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "api/boards", token);
        request.Content = JsonContent.Create(new { title });
        using var reply = await http.SendAsync(request, cancellationToken);
        await AuthorityClient.EnsureAsync(reply, $"create board '{title}'");
        return Guid.Parse((await reply.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetString()!);
    }

    public async Task DeleteAsync(string token, Guid board, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Delete, $"api/boards/{board:D}", token);
        using var reply = await http.SendAsync(request, cancellationToken);
        await AuthorityClient.EnsureAsync(reply, "delete a demo board");
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }
}
