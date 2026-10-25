namespace MiroClone.BoardsService.Boards;

/// <summary>One user's favourite on one board. Favourites are per user; a board's deletion removes them with it.</summary>
public sealed class BoardFavourite
{
    public Guid UserId { get; init; }

    public Guid BoardId { get; init; }

    public Board? Board { get; init; }
}
