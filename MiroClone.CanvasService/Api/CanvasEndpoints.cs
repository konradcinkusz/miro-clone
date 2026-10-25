using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MiroClone.CanvasService.Boards;
using MiroClone.CanvasService.Canvas;
using MiroClone.CanvasService.Persistence;
using MiroClone.ServiceDefaults;

namespace MiroClone.CanvasService.Api;

/// <summary>
/// The REST surface: read a board's canvas, and purge it. A read needs a token, a single organization and a board the caller may
/// see. A purge needs the caller's organization to own the snapshot, so it works after the board row is deleted (ADR 0016).
/// </summary>
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
        if (WorkspaceClaims.WorkspaceId(http.User) is not { } workspace)
        {
            return Results.NotFound();
        }

        var access = await CheckAccessAsync(boardId, http, boards, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        try
        {
            var canvas = await registry.GetOrLoadAsync(boardId, workspace, cancellationToken);
            return Results.Json(canvas.Snapshot(), CanvasJson.Options);
        }
        catch (CanvasStoreUnavailableException)
        {
            return Results.Problem(title: "Canvas store unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Purges the board's snapshot when the caller's organization owns it. A snapshot of another workspace answers 404, the same as
    /// a missing board. A board with no stored snapshot answers 204, so a retry from BoardsService is harmless.
    /// </summary>
    private static async Task<IResult> DeleteCanvasAsync(
        Guid boardId,
        HttpContext http,
        CanvasRegistry registry,
        CancellationToken cancellationToken)
    {
        if (WorkspaceClaims.WorkspaceId(http.User) is not { } workspace)
        {
            return Results.NotFound();
        }

        try
        {
            return await registry.PurgeAsync(boardId, workspace, cancellationToken) switch
            {
                PurgeOutcome.NotOwned => Results.NotFound(),
                _ => Results.NoContent(),
            };
        }
        catch (CanvasStoreUnavailableException)
        {
            return Results.Problem(title: "Canvas store unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
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
