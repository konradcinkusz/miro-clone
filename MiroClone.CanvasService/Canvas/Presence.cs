namespace MiroClone.CanvasService.Canvas;

/// <summary>
/// One participant's live state on a board. Identity (user id, name, colour) is stamped by the server from the token,
/// never taken from the client; the client sends only the cursor and the selection.
/// </summary>
public sealed record Presence(
    Guid UserId,
    string Name,
    string Color,
    Point? Cursor,
    IReadOnlyList<Guid> Selection);
