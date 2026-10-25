namespace MiroClone.CanvasService.Canvas;

/// <summary>What a mutation does to the layer order and content.</summary>
public enum MutationKind
{
    Upsert,
    Delete,
    Reorder,
}

/// <summary>
/// One change sent by a client. Only the fields the <see cref="Kind"/> names are read: <c>Layer</c> for an upsert,
/// <c>Id</c> for a delete, <c>Order</c> for a reorder.
/// </summary>
public sealed record LayerMutation(
    MutationKind Kind,
    Layer? Layer = null,
    Guid? Id = null,
    IReadOnlyList<Guid>? Order = null);

/// <summary>What happened when the aggregate applied a mutation.</summary>
public enum MutationStatus
{
    /// <summary>The board changed and its version advanced. Only this result is broadcast.</summary>
    Changed,

    /// <summary>The mutation was valid and already in effect. Replays land here, so they are harmless.</summary>
    Unchanged,

    /// <summary>An upsert would take the board past <see cref="LayerRules.MaxLayers"/>.</summary>
    LayerLimitReached,

    /// <summary>The mutation is malformed: a missing field, a duplicate in a reorder, or a rule break.</summary>
    Invalid,
}
