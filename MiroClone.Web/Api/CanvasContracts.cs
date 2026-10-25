namespace MiroClone.Web.Api;

/// <summary>Contract: CanvasState { version, layerIds, layers }. A copy; the round-trip test pins it to the frozen JSON shape.</summary>
public sealed record CanvasStateDto(long Version, IReadOnlyList<Guid> LayerIds, IReadOnlyDictionary<Guid, LayerDto> Layers);

/// <summary>Contract: Layer { id, type, x, y, width, height, fill, value?, points? }.</summary>
public sealed record LayerDto(
    Guid Id,
    string Type,
    double X,
    double Y,
    double Width,
    double Height,
    FillDto Fill,
    string? Value,
    IReadOnlyList<double[]>? Points);

/// <summary>Contract: fill { r, g, b }.</summary>
public sealed record FillDto(int R, int G, int B);
