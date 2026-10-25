using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using MiroClone.ServiceDefaults;

namespace MiroClone.BoardsService.Boards;

/// <summary>Operation names for the endpoints. Generated clients and the web app's typed client compile against these.</summary>
public static class BoardOperations
{
    public const string List = "ListBoards";
    public const string Create = "CreateBoard";
    public const string Rename = "RenameBoard";
    public const string Delete = "DeleteBoard";
    public const string SetFavourite = "SetBoardFavourite";
    public const string ClearFavourite = "ClearBoardFavourite";
    public const string Access = "CheckBoardAccess";
}

/// <summary>
/// The /api/boards surface (contract in docs/implementation-plan/README.md). Every route sits behind the authorization
/// group, so a request without a valid token is refused before any handler runs. Each id route answers 404 for a board
/// outside the caller's workspace.
/// </summary>
internal static class BoardEndpoints
{
    public static WebApplication MapBoardEndpoints(this WebApplication app)
    {
        var boards = app.MapGroup("/api/boards")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Api);

        boards.MapGet("", ListBoards).WithName(BoardOperations.List);
        boards.MapPost("", CreateBoard).WithName(BoardOperations.Create);
        boards.MapPatch("/{id:guid}", RenameBoard).WithName(BoardOperations.Rename);
        boards.MapDelete("/{id:guid}", DeleteBoard).WithName(BoardOperations.Delete);
        boards.MapPut("/{id:guid}/favourite", SetFavourite).WithName(BoardOperations.SetFavourite);
        boards.MapDelete("/{id:guid}/favourite", ClearFavourite).WithName(BoardOperations.ClearFavourite);
        boards.MapGet("/{id:guid}/access", CheckAccess).WithName(BoardOperations.Access);

        return app;
    }

    private static async Task<IResult> ListBoards(
        ClaimsPrincipal user,
        BoardService boards,
        string? search,
        bool? favourites,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!BoardCaller.TryRead(user, out var caller))
        {
            return NoWorkspace();
        }

        var result = await boards.ListAsync(
            caller,
            search,
            favouritesOnly: favourites ?? false,
            page: ListPaging.Page(page),
            pageSize: ListPaging.PageSize(pageSize),
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateBoard(
        ClaimsPrincipal user,
        BoardService boards,
        [FromBody] CreateBoardRequest request,
        CancellationToken cancellationToken)
    {
        if (!BoardCaller.TryRead(user, out var caller))
        {
            return NoWorkspace();
        }

        if (BoardTitle.Normalize(request.Title) is not { } title)
        {
            return BoardTitle.Invalid();
        }

        var board = await boards.CreateAsync(caller, title, cancellationToken);
        return Results.Created($"/api/boards/{board.Id:D}", board);
    }

    private static async Task<IResult> RenameBoard(
        ClaimsPrincipal user,
        BoardService boards,
        Guid id,
        [FromBody] RenameBoardRequest request,
        CancellationToken cancellationToken)
    {
        if (!BoardCaller.TryRead(user, out var caller))
        {
            return NoWorkspace();
        }

        if (BoardTitle.Normalize(request.Title) is not { } title)
        {
            return BoardTitle.Invalid();
        }

        var board = await boards.RenameAsync(caller, id, title, cancellationToken);
        return board is null ? BoardNotFound() : Results.Ok(board);
    }

    private static async Task<IResult> DeleteBoard(
        ClaimsPrincipal user,
        BoardService boards,
        HttpContext context,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!BoardCaller.TryRead(user, out var caller))
        {
            return NoWorkspace();
        }

        // The caller's bearer goes on to CanvasService with the purge, so the callee authorizes the purge itself.
        var authorization = context.Request.Headers.Authorization.ToString();
        var deleted = await boards.DeleteAsync(caller, id, authorization.Length > 0 ? authorization : null, cancellationToken);
        return deleted ? Results.NoContent() : BoardNotFound();
    }

    private static async Task<IResult> SetFavourite(
        ClaimsPrincipal user,
        BoardService boards,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!BoardCaller.TryRead(user, out var caller))
        {
            return NoWorkspace();
        }

        var found = await boards.SetFavouriteAsync(caller, id, favourite: true, cancellationToken);
        return found ? Results.NoContent() : BoardNotFound();
    }

    private static async Task<IResult> ClearFavourite(
        ClaimsPrincipal user,
        BoardService boards,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!BoardCaller.TryRead(user, out var caller))
        {
            return NoWorkspace();
        }

        var found = await boards.SetFavouriteAsync(caller, id, favourite: false, cancellationToken);
        return found ? Results.NoContent() : BoardNotFound();
    }

    private static async Task<IResult> CheckAccess(
        ClaimsPrincipal user,
        BoardService boards,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!BoardCaller.TryRead(user, out var caller))
        {
            return NoWorkspace();
        }

        return await boards.HasAccessAsync(caller, id, cancellationToken) ? Results.NoContent() : BoardNotFound();
    }

    /// <summary>A valid token with no active workspace cannot reach any board. Refused as forbidden, not defaulted.</summary>
    private static IResult NoWorkspace() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The token names no active workspace.");

    private static IResult BoardNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Board not found.");
}
