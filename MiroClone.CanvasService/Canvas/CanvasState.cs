namespace MiroClone.CanvasService.Canvas;

/// <summary>
/// The whole board as one value: the version, the layers in z-order (bottom first) and the layers by id.
/// This is the REST response and the snapshot document, so both read the same shape.
/// </summary>
public sealed record CanvasState(
    long Version,
    IReadOnlyList<Guid> LayerIds,
    IReadOnlyDictionary<Guid, Layer> Layers);
