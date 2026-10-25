namespace MiroClone.Web.Canvas.Core;

/// <summary>
/// Stacking order. Selected layers move as a group and keep their relative order. Ids the document does not hold
/// are ignored. The result is a full order: pass it to <see cref="LayerMutation.Reorder"/>.
/// </summary>
public static class LayerOrdering
{
    public static IReadOnlyList<Guid> BringToFront(IReadOnlyList<Guid> order, IEnumerable<Guid> selected)
    {
        ArgumentNullException.ThrowIfNull(order);
        var chosen = new HashSet<Guid>(selected);

        return order.Where(id => !chosen.Contains(id))
            .Concat(order.Where(chosen.Contains))
            .ToArray();
    }

    public static IReadOnlyList<Guid> SendToBack(IReadOnlyList<Guid> order, IEnumerable<Guid> selected)
    {
        ArgumentNullException.ThrowIfNull(order);
        var chosen = new HashSet<Guid>(selected);

        return order.Where(chosen.Contains)
            .Concat(order.Where(id => !chosen.Contains(id)))
            .ToArray();
    }
}
