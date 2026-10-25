namespace MiroClone.Web.Canvas.Core;

/// <summary>
/// The layers of one board. <see cref="Order"/> runs back to front: index 0 is the bottom layer, the last index is the top.
/// Read it freely; change it only through <see cref="MutationApplier"/>, so every change has an inverse for undo.
/// </summary>
public sealed class CanvasDocument
{
    public const int MaxLayers = 100;

    private readonly List<Guid> _order = [];
    private readonly Dictionary<Guid, Layer> _layers = [];

    public CanvasDocument()
    {
    }

    public int Count => _order.Count;

    public IReadOnlyList<Guid> Order => _order.AsReadOnly();

    public bool Contains(Guid id) => _layers.ContainsKey(id);

    public bool TryGet(Guid id, out Layer layer) => _layers.TryGetValue(id, out layer!);

    /// <summary>The layers from bottom to top.</summary>
    public IEnumerable<Layer> Ordered()
    {
        foreach (var id in _order)
        {
            yield return _layers[id];
        }
    }

    /// <summary>Loads a snapshot. Layers are given bottom to top, and the list may hold at most <see cref="MaxLayers"/> distinct ids.</summary>
    public static CanvasDocument Load(IEnumerable<Layer> backToFront)
    {
        ArgumentNullException.ThrowIfNull(backToFront);

        var document = new CanvasDocument();
        foreach (var layer in backToFront)
        {
            if (document.Count >= MaxLayers)
            {
                throw new ArgumentException($"A board holds at most {MaxLayers} layers.", nameof(backToFront));
            }

            if (!document._layers.TryAdd(layer.Id, layer))
            {
                throw new ArgumentException($"Layer {layer.Id} appears more than once.", nameof(backToFront));
            }

            document._order.Add(layer.Id);
        }

        return document;
    }

    internal void Append(Layer layer)
    {
        _layers.Add(layer.Id, layer);
        _order.Add(layer.Id);
    }

    internal void Replace(Layer layer) => _layers[layer.Id] = layer;

    internal void Remove(Guid id)
    {
        _layers.Remove(id);
        _order.Remove(id);
    }

    internal void SetOrder(IReadOnlyList<Guid> order)
    {
        _order.Clear();
        _order.AddRange(order);
    }
}
