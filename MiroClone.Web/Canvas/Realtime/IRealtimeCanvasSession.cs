using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;

namespace MiroClone.Web.Canvas.Realtime;

/// <summary>Whether the board's live connection is up. <see cref="Unavailable"/> is terminal for this session: the page says so and stops retrying.</summary>
public enum CanvasConnectionState
{
    Connecting,
    Live,
    Reconnecting,
    Unavailable,
}

/// <summary>One person on the board, once per user. <see cref="IsSelf"/> marks the signed-in user's own entry.</summary>
public sealed record ParticipantView(Guid UserId, string Name, string Color, bool IsSelf)
{
    /// <summary>The avatar letter: the first letter of the name, upper case.</summary>
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";
}

/// <summary>Another participant's pointer and selection, in world coordinates, for drawing on the canvas.</summary>
public sealed record RemoteParticipant(Guid UserId, string Name, string Color, Point? Cursor, IReadOnlyList<Bounds> Selection);

/// <summary>
/// A canvas session that is also live: it sends local edits to the room, applies the room's edits, and shares presence.
/// The rules of an edit stay in <see cref="ICanvasSession.Commit"/>; the rules of a remote edit stay in <see cref="MutationApplier"/>.
/// </summary>
public interface IRealtimeCanvasSession : ICanvasSession
{
    CanvasConnectionState Connection { get; }

    /// <summary>Everyone on the board, the signed-in user first and each user once.</summary>
    IReadOnlyList<ParticipantView> Participants { get; }

    /// <summary>The other participants' cursors and selections. The signed-in user is never listed here.</summary>
    IReadOnlyList<RemoteParticipant> RemoteParticipants { get; }

    /// <summary>
    /// Raised when the document, the participants or the connection changed. It is raised on the thread the session was opened
    /// on, so a listener may touch the page after an <c>InvokeAsync</c> hop and needs no lock of its own.
    /// </summary>
    event Action? Changed;

    /// <summary>Starts the connection and its background work. Calling it again does nothing.</summary>
    void Start();

    /// <summary>Records this user's cursor (world coordinates, or null when it is off the canvas) and selection. Sent at most 20 times a second.</summary>
    void SetPresence(Point? cursor, IReadOnlyList<Guid> selection);
}

/// <summary>Opens a live session for one board. The page calls it once it has the board's layers.</summary>
public interface ICanvasSessionFactory
{
    /// <param name="dispatcher">Where the session runs its changes: the page's own dispatcher, so changes reach the page as its events do.</param>
    IRealtimeCanvasSession Open(string sessionId, Guid selfUserId, string selfName, Guid boardId, CanvasDocument initial, SynchronizationContext? dispatcher);
}
