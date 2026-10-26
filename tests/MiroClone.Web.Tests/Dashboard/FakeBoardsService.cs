using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using MiroClone.Web.Api;
using MiroClone.Web.Components.Dashboard;
using MiroClone.Web.Tests.TestSupport;

namespace MiroClone.Web.Tests.Dashboard;

/// <summary>
/// An in-memory boards service behind the typed client: lists page and filter as the contract says, and creates, renames, deletes
/// and favourites change what the next list returns. Every call is logged; <see cref="FailNext"/> answers the next call with a status.
/// </summary>
internal sealed class FakeBoardsService : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private readonly List<BoardSummaryDto> _boards = [];

    public List<(string Method, string Path, string Query, string? Body)> Calls { get; } = [];

    public HttpStatusCode? FailNext { get; set; }

    public Guid User { get; init; }

    public DateTimeOffset Now { get; init; }

    public IReadOnlyList<BoardSummaryDto> Boards => _boards;

    public FakeBoardsService Add(params BoardSummaryDto[] boards)
    {
        _boards.AddRange(boards);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var query = request.RequestUri.Query.TrimStart('?');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request.Method.Method, path, query, body));

        if (FailNext is { } status)
        {
            FailNext = null;
            return ScriptedHandler.Reply(status, new { title = "refused" });
        }

        var segments = path.Split('/');
        if (path == "api/boards")
        {
            return request.Method == HttpMethod.Get
                ? List(QueryHelpers.ParseQuery(query))
                : Create(body!);
        }

        if (segments is ["api", "boards", var idText, ..] && Guid.TryParse(idText, out var id))
        {
            var index = _boards.FindIndex(board => board.Id == id);
            if (index < 0)
            {
                return ScriptedHandler.Reply(HttpStatusCode.NotFound, new { title = "Board not found." });
            }

            if (segments is ["api", "boards", _, "favourite"])
            {
                _boards[index] = _boards[index] with { IsFavourite = request.Method == HttpMethod.Put };
                return ScriptedHandler.Reply(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Patch)
            {
                _boards[index] = _boards[index] with { Title = JsonSerializer.Deserialize<RenameBoardRequest>(body!, Wire)!.Title };
                return ScriptedHandler.Reply(HttpStatusCode.OK, _boards[index]);
            }

            _boards.RemoveAt(index);
            return ScriptedHandler.Reply(HttpStatusCode.NoContent);
        }

        return ScriptedHandler.Reply(HttpStatusCode.NotFound, new { title = "No route." });
    }

    private HttpResponseMessage List(Dictionary<string, StringValues> query)
    {
        var rows = _boards.OrderByDescending(board => board.CreatedAt).AsEnumerable();

        if (query.TryGetValue("search", out var search) && !StringValues.IsNullOrEmpty(search))
        {
            rows = rows.Where(board => board.Title.Contains(search.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        if (query.TryGetValue("favourites", out var favourites) && favourites == "true")
        {
            rows = rows.Where(board => board.IsFavourite);
        }

        var matches = rows.ToList();
        var page = query.TryGetValue("page", out var pageText) ? int.Parse(pageText.ToString(), System.Globalization.CultureInfo.InvariantCulture) : 1;
        var items = matches.Skip((page - 1) * BoardQuery.PageSize).Take(BoardQuery.PageSize).ToList();
        return ScriptedHandler.Reply(HttpStatusCode.OK, new { items, total = matches.Count });
    }

    private HttpResponseMessage Create(string body)
    {
        var title = JsonSerializer.Deserialize<CreateBoardRequest>(body, Wire)!.Title;
        var board = new BoardSummaryDto(Guid.NewGuid(), title, User, "Ada", 1, Now, IsFavourite: false);
        _boards.Add(board);
        return ScriptedHandler.Reply(HttpStatusCode.Created, board);
    }
}
