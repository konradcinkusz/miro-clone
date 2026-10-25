namespace MiroClone.CanvasService.Canvas;

/// <summary>
/// The canvas aggregate for one board: layers, their z-order and a version that advances on every real change.
/// It is synchronous and holds no I/O, so every rule is testable without a host. Thread-safe on its own lock.
/// </summary>
/// <remarks>
/// <see cref="Gate"/> is separate: it orders the hub's broadcasts with joins, so a joining client receives every
/// change either inside its snapshot or as a broadcast after it, never both missing. The aggregate does not use it.
/// </remarks>
public sealed class BoardCanvas
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Layer> _layers = [];
    private readonly List<Guid> _order = [];

    private BoardCanvas(Guid boardId, long version)
    {
        BoardId = boardId;
        Version = version;
    }

    public Guid BoardId { get; }

    /// <summary>Serialises hub broadcasts and joins for this board. Acquire before applying a mutation and broadcasting it.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public long Version { get; private set; }

    public static BoardCanvas Empty(Guid boardId) => new(boardId, version: 0);

    /// <summary>Rebuilds the aggregate from a stored state, keeping only the layers the order names.</summary>
    /// <exception cref="InvalidOperationException">The state holds more layers than the board allows.</exception>
    public static BoardCanvas Restore(Guid boardId, CanvasState state)
    {
        if (state.LayerIds.Count > LayerRules.MaxLayers)
        {
            throw new InvalidOperationException($"Stored canvas {boardId} holds {state.LayerIds.Count} layers, above the limit.");
        }

        var canvas = new BoardCanvas(boardId, state.Version);
        foreach (var id in state.LayerIds)
        {
            if (state.Layers.TryGetValue(id, out var layer) && canvas._layers.TryAdd(id, layer))
            {
                canvas._order.Add(id);
            }
        }

        return canvas;
    }

    /// <summary>Applies one mutation. Only <see cref="MutationStatus.Changed"/> advances the version.</summary>
    public MutationStatus Apply(LayerMutation mutation)
    {
        lock (_lock)
        {
            return mutation.Kind switch
            {
                MutationKind.Upsert => Upsert(mutation.Layer),
                MutationKind.Delete => Delete(mutation.Id),
                MutationKind.Reorder => Reorder(mutation.Order),
                _ => MutationStatus.Invalid,
            };
        }
    }

    /// <summary>A copy of the whole board. Safe to serialise outside the lock.</summary>
    public CanvasState Snapshot()
    {
        lock (_lock)
        {
            return new CanvasState(Version, [.. _order], new Dictionary<Guid, Layer>(_layers));
        }
    }

    private MutationStatus Upsert(Layer? layer)
    {
        if (layer is null || LayerRules.Validate(layer) is not null)
        {
            return MutationStatus.Invalid;
        }

        if (_layers.TryGetValue(layer.Id, out var existing))
        {
            if (LayerEquality.Same(existing, layer))
            {
                return MutationStatus.Unchanged;
            }

            _layers[layer.Id] = layer;
        }
        else
        {
            if (_layers.Count >= LayerRules.MaxLayers)
            {
                return MutationStatus.LayerLimitReached;
            }

            _layers.Add(layer.Id, layer);
            _order.Add(layer.Id);
        }

        return Advance();
    }

    private MutationStatus Delete(Guid? id)
    {
        if (id is not { } layerId)
        {
            return MutationStatus.Invalid;
        }

        // Deleting an absent layer is a replay of a delete that already took effect.
        if (!_layers.Remove(layerId))
        {
            return MutationStatus.Unchanged;
        }

        _order.Remove(layerId);
        return Advance();
    }

    /// <summary>
    /// Listed layers take the bottom of the stack in the order given. Layers the client did not list keep their
    /// relative order on top. Unknown ids are ignored, so a reorder that races a delete still converges.
    /// </summary>
    private MutationStatus Reorder(IReadOnlyList<Guid>? requested)
    {
        if (requested is null || requested.Distinct().Count() != requested.Count)
        {
            return MutationStatus.Invalid;
        }

        var listed = requested.Where(_layers.ContainsKey).ToList();
        var listedSet = listed.ToHashSet();
        var reordered = listed.Concat(_order.Where(id => !listedSet.Contains(id))).ToList();
        if (reordered.SequenceEqual(_order))
        {
            return MutationStatus.Unchanged;
        }

        _order.Clear();
        _order.AddRange(reordered);
        return Advance();
    }

    private MutationStatus Advance()
    {
        Version++;
        return MutationStatus.Changed;
    }
}

/// <summary>Field-by-field equality for layers, so an identical replay is detected even though the lists differ by reference.</summary>
public static class LayerEquality
{
    public static bool Same(Layer a, Layer b) =>
        a.Id == b.Id
        && a.Type == b.Type
        && a.X == b.X
        && a.Y == b.Y
        && a.Width == b.Width
        && a.Height == b.Height
        && a.Fill == b.Fill
        && a.Value == b.Value
        && (a.Points is null
            ? b.Points is null
            : b.Points is not null && a.Points.SequenceEqual(b.Points));
}
