namespace MiroClone.Web.Canvas.Core;

public enum MutationResult
{
    /// <summary>The document changed.</summary>
    Applied,

    /// <summary>The document already held this state, so nothing changed.</summary>
    Unchanged,

    /// <summary>A new layer would exceed <see cref="CanvasDocument.MaxLayers"/>. Maps to HTTP 409 on the server.</summary>
    LimitReached,

    /// <summary>The mutation is malformed or holds values that are not finite or are out of range.</summary>
    Invalid,
}

/// <param name="Result">What happened.</param>
/// <param name="Inverse">The mutations that undo this one, in the order to apply them. Empty unless the result is Applied.</param>
public readonly record struct MutationOutcome(MutationResult Result, IReadOnlyList<LayerMutation> Inverse);

/// <summary>
/// Applies one mutation to a document. Idempotent: applying a mutation twice leaves the document as applying it once
/// did, and the second call returns <see cref="MutationResult.Unchanged"/>. Last writer wins per layer. Remote
/// mutations and local edits both go through here, so the rules live in one place.
/// </summary>
public static class MutationApplier
{
    private static readonly IReadOnlyList<LayerMutation> NoMutations = [];

    public static MutationOutcome Apply(CanvasDocument document, LayerMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mutation);

        return mutation.Kind switch
        {
            LayerMutationKind.Upsert => Upsert(document, mutation.Layer),
            LayerMutationKind.Delete => Delete(document, mutation.Id),
            LayerMutationKind.Reorder => Reorder(document, mutation.Order),
            _ => Rejected(),
        };
    }

    private static MutationOutcome Upsert(CanvasDocument document, Layer? layer)
    {
        if (layer is null || !IsWellFormed(layer))
        {
            return Rejected();
        }

        if (document.TryGet(layer.Id, out var existing))
        {
            if (existing.HasSameContent(layer))
            {
                return Unchanged();
            }

            document.Replace(layer);
            return new MutationOutcome(MutationResult.Applied, [LayerMutation.Upsert(existing)]);
        }

        if (document.Count >= CanvasDocument.MaxLayers)
        {
            return new MutationOutcome(MutationResult.LimitReached, NoMutations);
        }

        document.Append(layer);
        return new MutationOutcome(MutationResult.Applied, [LayerMutation.Delete(layer.Id)]);
    }

    private static MutationOutcome Delete(CanvasDocument document, Guid? id)
    {
        if (id is not { } layerId)
        {
            return Rejected();
        }

        if (!document.TryGet(layerId, out var removed))
        {
            return Unchanged();
        }

        var orderBefore = document.Order.ToArray();
        document.Remove(layerId);

        // Re-adding the layer puts it on top; the reorder puts it back where it was.
        return new MutationOutcome(
            MutationResult.Applied,
            [LayerMutation.Upsert(removed), LayerMutation.Reorder(orderBefore)]);
    }

    private static MutationOutcome Reorder(CanvasDocument document, IReadOnlyList<Guid>? order)
    {
        if (order is null)
        {
            return Rejected();
        }

        // Known ids keep the requested order. Ids the document does not hold are ignored. Layers the request
        // leaves out keep their current relative order at the top.
        var orderBefore = document.Order.ToArray();
        var seen = new HashSet<Guid>();
        var next = new List<Guid>(orderBefore.Length);

        foreach (var id in order)
        {
            if (document.Contains(id) && seen.Add(id))
            {
                next.Add(id);
            }
        }

        foreach (var id in orderBefore)
        {
            if (seen.Add(id))
            {
                next.Add(id);
            }
        }

        if (next.SequenceEqual(orderBefore))
        {
            return Unchanged();
        }

        document.SetOrder(next);
        return new MutationOutcome(MutationResult.Applied, [LayerMutation.Reorder(orderBefore)]);
    }

    /// <summary>Checks the fields a remote peer could send wrong: non-finite numbers, negative sizes, and points on the wrong type.</summary>
    internal static bool IsWellFormed(Layer layer)
    {
        if (!IsFinite(layer.X) || !IsFinite(layer.Y) || !IsFinite(layer.Width) || !IsFinite(layer.Height))
        {
            return false;
        }

        if (layer.Width < 0 || layer.Height < 0)
        {
            return false;
        }

        if (layer.Type != LayerType.Path)
        {
            return layer.Points is null;
        }

        return layer.Points is { Count: > 0 } points && points.All(p => IsFinite(p.X) && IsFinite(p.Y));
    }

    private static bool IsFinite(double value) => double.IsFinite(value);

    private static MutationOutcome Unchanged() => new(MutationResult.Unchanged, NoMutations);

    private static MutationOutcome Rejected() => new(MutationResult.Invalid, NoMutations);
}
