using MiroClone.CanvasService.Canvas;

namespace MiroClone.CanvasService.Realtime;

/// <summary>The wire names of the frozen hub contract (docs/implementation-plan/README.md). Changing one is a contract change.</summary>
public static class CanvasHubMessages
{
    public const string HubPath = "/hubs/canvas";

    // server → client
    public const string InitialState = "InitialState";
    public const string LayerMutation = "LayerMutation";
    public const string PresenceUpdated = "PresenceUpdated";
    public const string PresenceLeft = "PresenceLeft";

    // Error codes, sent as the HubException message. The web client maps them to user-facing text.
    public const string NotFound = "board_not_found";
    public const string NotJoined = "not_joined";
    public const string Unauthenticated = "unauthenticated";
    public const string InvalidLayer = "invalid_layer";
    public const string InvalidPresence = "invalid_presence";
    public const string LayerLimit = "layer_limit";
    public const string RateLimited = "rate_limited";
    public const string Unavailable = "canvas_unavailable";
}

/// <summary>
/// Who is on each board, keyed by connection. A user with two tabs appears twice, once per connection. Lives in memory:
/// presence is ephemeral and is rebuilt as clients reconnect, so it is never snapshotted.
/// </summary>
public sealed class PresenceTracker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Dictionary<string, Presence>> _boards = [];

    public void Upsert(Guid boardId, string connectionId, Presence presence)
    {
        lock (_lock)
        {
            if (!_boards.TryGetValue(boardId, out var connections))
            {
                connections = [];
                _boards.Add(boardId, connections);
            }

            connections[connectionId] = presence;
        }
    }

    public void Remove(Guid boardId, string connectionId)
    {
        lock (_lock)
        {
            if (_boards.TryGetValue(boardId, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    _boards.Remove(boardId);
                }
            }
        }
    }

    public IReadOnlyList<Presence> Snapshot(Guid boardId)
    {
        lock (_lock)
        {
            return _boards.TryGetValue(boardId, out var connections) ? [.. connections.Values] : [];
        }
    }
}
