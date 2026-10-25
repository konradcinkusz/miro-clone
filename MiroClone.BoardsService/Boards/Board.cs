namespace MiroClone.BoardsService.Boards;

/// <summary>
/// A board inside one workspace (authservice organization). The author's name is copied at creation: this service
/// holds no user directory (P3), so a later rename of the user does not rewrite the board's history.
/// </summary>
public sealed class Board
{
    public Guid Id { get; init; }

    public Guid WorkspaceId { get; init; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// The title lowered with the invariant culture, written on every rename. Search compares against this column, so
    /// the match does not depend on the database's case folding: SQLite folds ASCII only, PostgreSQL folds by locale.
    /// </summary>
    public string TitleSearch { get; private set; } = string.Empty;

    public Guid AuthorId { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    /// <summary>Picks one of the static preview images in the web app, 1 to 10 (contract: imageSeed).</summary>
    public int ImageSeed { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public List<BoardFavourite> Favourites { get; } = [];

    public void Rename(string title)
    {
        Title = title;
        TitleSearch = title.ToLowerInvariant();
    }
}
