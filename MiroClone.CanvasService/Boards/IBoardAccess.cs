namespace MiroClone.CanvasService.Boards;

/// <summary>The answer BoardsService gives for one caller and one board.</summary>
public enum BoardAccess
{
    Allowed,

    /// <summary>Not in the caller's workspace, or deleted. Both answer 404, never 403 (frozen contract).</summary>
    NotFound,
}

/// <summary>
/// Asks BoardsService whether the caller may see a board. CanvasService owns no board data (ADR 0003), so membership is
/// always decided by the owner of the boards table.
/// </summary>
public interface IBoardAccess
{
    /// <exception cref="BoardsServiceException">BoardsService could not give an answer. The caller must fail closed.</exception>
    Task<BoardAccess> CheckAsync(Guid boardId, string bearerToken, CancellationToken cancellationToken);
}

/// <summary>BoardsService could not answer. <see cref="StatusCode"/> is what this service returns to its own caller.</summary>
public sealed class BoardsServiceException(int statusCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>503 when BoardsService is unreachable or failing; 502 when it answers with a redirect (a configuration bug).</summary>
    public int StatusCode { get; } = statusCode;
}
