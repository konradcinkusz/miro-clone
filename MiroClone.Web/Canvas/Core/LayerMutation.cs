namespace MiroClone.Web.Canvas.Core;

/// <summary>Mirrors the <c>LayerMutation</c> contract (docs/implementation-plan/README.md).</summary>
public enum LayerMutationKind
{
    Upsert,
    Delete,
    Reorder,
}

/// <summary>
/// One change to a board. Build it with the factory methods; <see cref="MutationApplier"/> rejects a mutation
/// whose fields do not match its kind.
/// </summary>
public sealed record LayerMutation(
    LayerMutationKind Kind,
    Layer? Layer = null,
    Guid? Id = null,
    IReadOnlyList<Guid>? Order = null)
{
    public static LayerMutation Upsert(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new LayerMutation(LayerMutationKind.Upsert, Layer: layer);
    }

    public static LayerMutation Delete(Guid id) => new(LayerMutationKind.Delete, Id: id);

    public static LayerMutation Reorder(IReadOnlyList<Guid> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new LayerMutation(LayerMutationKind.Reorder, Order: order);
    }
}
