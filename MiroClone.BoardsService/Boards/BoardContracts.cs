using System.Security.Claims;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService.Boards;

/// <summary>Body of POST /api/boards.</summary>
public sealed record CreateBoardRequest(string? Title);

/// <summary>Body of PATCH /api/boards/{id}.</summary>
public sealed record RenameBoardRequest(string? Title);

/// <summary>The board as the dashboard lists it (contract: BoardSummary). Camel-cased on the wire.</summary>
public sealed record BoardSummary(
    Guid Id,
    string Title,
    Guid AuthorId,
    string AuthorName,
    int ImageSeed,
    DateTimeOffset CreatedAt,
    bool IsFavourite);

/// <summary>One page of boards and the total across all pages (contract: { items, total }).</summary>
public sealed record BoardPage(IReadOnlyList<BoardSummary> Items, int Total);

/// <summary>
/// The caller, read from the validated token. Built only when both the user and the active workspace are present,
/// so no query can run without a tenant.
/// </summary>
public readonly record struct BoardCaller(Guid UserId, Guid WorkspaceId, string AuthorName)
{
    public const int AuthorNameMaxLength = 200;

    private const string FallbackName = "Member";

    public static bool TryRead(ClaimsPrincipal principal, out BoardCaller caller)
    {
        var userId = WorkspaceClaims.UserId(principal);
        var workspaceId = WorkspaceClaims.WorkspaceId(principal);
        if (userId is null || workspaceId is null)
        {
            caller = default;
            return false;
        }

        var name = principal.FindFirst("name")?.Value;
        var authorName = string.IsNullOrWhiteSpace(name) ? FallbackName : name.Trim();
        caller = new BoardCaller(userId.Value, workspaceId.Value, Truncate(authorName));
        return true;
    }

    private static string Truncate(string value) =>
        value.Length <= AuthorNameMaxLength ? value : value[..AuthorNameMaxLength];
}
