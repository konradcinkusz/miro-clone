namespace MiroClone.Web.Api;

/// <summary>The board as the dashboard lists it (contract: BoardSummary). A copy of the boards service's record; round-trip tested.</summary>
public sealed record BoardSummaryDto(
    Guid Id,
    string Title,
    Guid AuthorId,
    string AuthorName,
    int ImageSeed,
    DateTimeOffset CreatedAt,
    bool IsFavourite);

/// <summary>One page of boards and the total across all pages (contract: { items, total }).</summary>
public sealed record BoardPageDto(IReadOnlyList<BoardSummaryDto> Items, int Total);

/// <summary>The body of POST /api/boards (contract). A copy of the boards service's request record; round-trip tested.</summary>
public sealed record CreateBoardRequest(string Title);

/// <summary>The body of PATCH /api/boards/{id} (contract). A copy of the boards service's request record; round-trip tested.</summary>
public sealed record RenameBoardRequest(string Title);
