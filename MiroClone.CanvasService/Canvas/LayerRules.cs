namespace MiroClone.CanvasService.Canvas;

/// <summary>The rules a layer must meet before the board accepts it. Pure functions, no I/O.</summary>
public static class LayerRules
{
    /// <summary>Layers per board (frozen contract). The 101st upsert is refused, never truncated.</summary>
    public const int MaxLayers = 100;

    /// <summary>Longest text or note value, in characters.</summary>
    public const int MaxValueLength = 5000;

    /// <summary>Vertices in one pencil stroke.</summary>
    public const int MaxPathPoints = 2000;

    /// <summary>Selected layers in one presence update, bounded by the layer cap.</summary>
    public const int MaxSelection = MaxLayers;

    /// <summary>Returns the first rule the layer breaks, or null when it is valid.</summary>
    public static string? Validate(Layer layer)
    {
        if (layer.Id == Guid.Empty)
        {
            return "Layer id is required.";
        }

        if (!IsFinite(layer.X) || !IsFinite(layer.Y) || !IsFinite(layer.Width) || !IsFinite(layer.Height))
        {
            return "Layer geometry must be finite numbers.";
        }

        if (layer.Width < 0 || layer.Height < 0)
        {
            return "Layer width and height cannot be negative.";
        }

        if (layer.Value is { Length: > MaxValueLength })
        {
            return $"Layer value is longer than {MaxValueLength} characters.";
        }

        if (layer.Value is not null && layer.Type is not (LayerType.Text or LayerType.Note))
        {
            return "Only text and note layers carry a value.";
        }

        if (layer.Type is LayerType.Path)
        {
            return ValidatePath(layer.Points);
        }

        return layer.Points is null ? null : "Only path layers carry points.";
    }

    /// <summary>A selection must hold distinct ids and stay within the layer cap.</summary>
    public static bool IsValidSelection(IReadOnlyList<Guid> selection) =>
        selection.Count <= MaxSelection && selection.Distinct().Count() == selection.Count;

    private static string? ValidatePath(IReadOnlyList<PathPoint>? points)
    {
        if (points is null or { Count: 0 })
        {
            return "A path needs at least one point.";
        }

        if (points.Count > MaxPathPoints)
        {
            return $"A path holds at most {MaxPathPoints} points.";
        }

        return points.All(point => IsFinite(point.X) && IsFinite(point.Y))
            ? null
            : "Path points must be finite numbers.";
    }

    private static bool IsFinite(double value) => double.IsFinite(value);
}
