using MiroClone.Web.Api;

namespace MiroClone.Web.Canvas.Realtime;

/// <summary>
/// The hub's wire types, copied from the canvas service (docs/implementation-plan/README.md, <c>LayerMutation</c> and
/// <c>Presence</c>). <see cref="Kind"/> is a string so the client needs no enum converter; the round-trip tests pin it to the
/// service's enum names.
/// </summary>
public sealed record LayerMutationDto(string Kind, LayerDto? Layer, Guid? Id, IReadOnlyList<Guid>? Order);

/// <summary>Contract: Presence { userId, name, color, cursor?, selection }. The service stamps identity; the client sends the cursor and selection.</summary>
public sealed record PresenceDto(Guid UserId, string Name, string Color, CursorDto? Cursor, IReadOnlyList<Guid> Selection);

/// <summary>Contract: a point in world coordinates, as <c>{ x, y }</c>.</summary>
public sealed record CursorDto(double X, double Y);

/// <summary>The hub's server-to-client method names (docs/implementation-plan/README.md).</summary>
internal static class HubMethods
{
    public const string Path = "/hubs/canvas";

    public const string JoinBoard = "JoinBoard";
    public const string SendMutation = "SendMutation";
    public const string UpdatePresence = "UpdatePresence";

    public const string InitialState = "InitialState";
    public const string LayerMutation = "LayerMutation";
    public const string PresenceUpdated = "PresenceUpdated";
    public const string PresenceLeft = "PresenceLeft";

    /// <summary>The error codes the service sends as the hub exception message (CanvasHubMessages).</summary>
    public const string BoardNotFound = "board_not_found";
    public const string Unauthenticated = "unauthenticated";
    public const string RateLimited = "rate_limited";
}
