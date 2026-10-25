using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.CanvasService.Api;

/// <summary>The REST surface: read a board's canvas, purge it. Every route needs a token and a board the caller may see.</summary>
public static class CanvasEndpoints
{
    public const string GetCanvasName = "GetCanvas";
    public const string DeleteCanvasName = "DeleteCanvas";

    public static WebApplication MapCanvasEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/canvas")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Api);

        api.MapGet("/{boardId:guid}", GetCanvasAsync).WithName(GetCanvasName);
        api.MapDelete("/{boardId:guid}", DeleteCanvasAsync).WithName(DeleteCanvasName);
        return app;
    }

    private static async Task<IResult> GetCanvasAsync(
        Guid boardId,
        HttpContext http,
        IBoardAccess boards,
        CanvasRegistry registry,
        CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(boardId, http, boards, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        try
        {
            var canvas = await registry.GetOrLoadAsync(boardId, cancellationToken);
            return Results.Json(canvas.Snapshot(), CanvasJson.Options);
        }
        catch (CanvasStoreUnavailableException)
        {
            return Results.Problem(title: "Canvas store unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Purges the board's canvas. The caller must still be allowed to see the board, so this cannot be used to purge another
    /// workspace's canvas. BoardsService calls it after deleting a board (ADR 0012 records what that means in practice).
    /// </summary>
    private static async Task<IResult> DeleteCanvasAsync(
        Guid boardId,
        HttpContext http,
        IBoardAccess boards,
        CanvasRegistry registry,
        CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(boardId, http, boards, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        // A failed store delete is retried on each tick by the registry. The board is gone from memory either way.
        await registry.RemoveAsync(boardId, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Null when the caller may see the board; otherwise the answer to return. A non-member gets 404, never 403.</summary>
    private static async Task<IResult?> CheckAccessAsync(
        Guid boardId,
        HttpContext http,
        IBoardAccess boards,
        CancellationToken cancellationToken)
    {
        var token = BearerToken.From(http.Request);
        if (token is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return await boards.CheckAsync(boardId, token, cancellationToken) == BoardAccess.Allowed
                ? null
                : Results.NotFound();
        }
        catch (BoardsServiceException ex)
        {
            return Results.Problem(title: "Board access could not be checked.", statusCode: ex.StatusCode);
        }
    }
}
