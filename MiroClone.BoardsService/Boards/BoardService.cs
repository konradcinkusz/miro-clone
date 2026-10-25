using Microsoft.EntityFrameworkCore;
using MiroClone.BoardsService.Canvas;
using MiroClone.BoardsService.Persistence;

namespace MiroClone.BoardsService.Boards;

/// <summary>
/// The boards rules. Every query is filtered by the caller's workspace, so another workspace's board reads as absent
/// (404, never 403). Methods that take a board id return null or false for "not in your workspace" and the endpoint
/// answers 404.
/// </summary>
internal sealed class BoardService(BoardsDbContext database, CanvasPurgeClient canvas, TimeProvider clock)
{
    private const int MinImageSeed = 1;
    private const int MaxImageSeed = 10;

    public async Task<BoardPage> ListAsync(
        BoardCaller caller,
        string? search,
        bool favouritesOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var boards = database.Boards.AsNoTracking().Where(board => board.WorkspaceId == caller.WorkspaceId);

        // Case-insensitive "contains" against the stored invariant-lowercase key (see Board.TitleSearch).
        var term = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            boards = boards.Where(board => board.TitleSearch.Contains(term));
        }

        if (favouritesOnly)
        {
            boards = boards.Where(board => board.Favourites.Any(favourite => favourite.UserId == caller.UserId));
        }

        var total = await boards.CountAsync(cancellationToken);
        var items = await boards
            .OrderByDescending(board => board.CreatedAt)
            .ThenBy(board => board.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(board => new BoardSummary(
                board.Id,
                board.Title,
                board.AuthorId,
                board.AuthorName,
                board.ImageSeed,
                board.CreatedAt,
                board.Favourites.Any(favourite => favourite.UserId == caller.UserId)))
            .ToListAsync(cancellationToken);

        return new BoardPage(items, total);
    }

    public async Task<BoardSummary> CreateAsync(BoardCaller caller, string title, CancellationToken cancellationToken)
    {
        var board = new Board
        {
            Id = Guid.NewGuid(),
            WorkspaceId = caller.WorkspaceId,
            AuthorId = caller.UserId,
            AuthorName = caller.AuthorName,
            ImageSeed = Random.Shared.Next(MinImageSeed, MaxImageSeed + 1),
            CreatedAt = clock.GetUtcNow(),
        };
        board.Rename(title);

        database.Boards.Add(board);
        await database.SaveChangesAsync(cancellationToken);

        return ToSummary(board, isFavourite: false);
    }

    public async Task<BoardSummary?> RenameAsync(BoardCaller caller, Guid boardId, string title, CancellationToken cancellationToken)
    {
        var board = await FindInWorkspaceAsync(caller, boardId, cancellationToken);
        if (board is null)
        {
            return null;
        }

        board.Rename(title);
        await database.SaveChangesAsync(cancellationToken);

        var isFavourite = await IsFavouriteAsync(caller, boardId, cancellationToken);
        return ToSummary(board, isFavourite);
    }

    /// <summary>
    /// Deletes the board and its favourites in one transaction, then purges the canvas snapshot after the commit. A purge
    /// that fails does not undo the delete: the snapshot is an orphan, which nothing reads (ADR 0013).
    /// </summary>
    public async Task<bool> DeleteAsync(BoardCaller caller, Guid boardId, string? authorization, CancellationToken cancellationToken)
    {
        var board = await database.Boards
            .Include(candidate => candidate.Favourites)
            .FirstOrDefaultAsync(candidate => candidate.Id == boardId && candidate.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (board is null)
        {
            return false;
        }

        database.BoardFavourites.RemoveRange(board.Favourites);
        database.Boards.Remove(board);
        await database.SaveChangesAsync(cancellationToken);

        // The purge logs its own failure; the delete has already committed and stays committed either way.
        await canvas.PurgeAsync(boardId, authorization, cancellationToken);
        return true;
    }

    /// <summary>Marks or unmarks a favourite. Idempotent: setting a favourite that exists, or clearing one that does not, succeeds.</summary>
    public async Task<bool> SetFavouriteAsync(BoardCaller caller, Guid boardId, bool favourite, CancellationToken cancellationToken)
    {
        var exists = await database.Boards.AnyAsync(
            board => board.Id == boardId && board.WorkspaceId == caller.WorkspaceId,
            cancellationToken);
        if (!exists)
        {
            return false;
        }

        var existing = await database.BoardFavourites.FirstOrDefaultAsync(
            row => row.UserId == caller.UserId && row.BoardId == boardId,
            cancellationToken);

        if (favourite && existing is null)
        {
            database.BoardFavourites.Add(new BoardFavourite { UserId = caller.UserId, BoardId = boardId });
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent request set the same favourite first. The outcome is the one the caller asked for.
                if (!await IsFavouriteAsync(caller, boardId, cancellationToken))
                {
                    throw;
                }

                database.ChangeTracker.Clear();
            }
        }
        else if (!favourite && existing is not null)
        {
            database.BoardFavourites.Remove(existing);
            await database.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>Whether the board is in the caller's workspace. CanvasService asks this before it serves a board's canvas.</summary>
    public Task<bool> HasAccessAsync(BoardCaller caller, Guid boardId, CancellationToken cancellationToken) =>
        database.Boards.AnyAsync(
            board => board.Id == boardId && board.WorkspaceId == caller.WorkspaceId,
            cancellationToken);

    private Task<Board?> FindInWorkspaceAsync(BoardCaller caller, Guid boardId, CancellationToken cancellationToken) =>
        database.Boards.FirstOrDefaultAsync(
            board => board.Id == boardId && board.WorkspaceId == caller.WorkspaceId,
            cancellationToken);

    private Task<bool> IsFavouriteAsync(BoardCaller caller, Guid boardId, CancellationToken cancellationToken) =>
        database.BoardFavourites.AnyAsync(
            row => row.UserId == caller.UserId && row.BoardId == boardId,
            cancellationToken);

    private static BoardSummary ToSummary(Board board, bool isFavourite) => new(
        board.Id,
        board.Title,
        board.AuthorId,
        board.AuthorName,
        board.ImageSeed,
        board.CreatedAt,
        isFavourite);
}
