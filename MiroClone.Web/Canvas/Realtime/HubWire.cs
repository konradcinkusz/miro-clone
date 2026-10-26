using System.Text.RegularExpressions;
using MiroClone.Web.Api;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;

namespace MiroClone.Web.Canvas.Realtime;

/// <summary>
/// Converts between the hub's wire types and the Core rules. Inbound values are checked here for shape only; the rules
/// (finite numbers, the layer cap, valid reorders) are <see cref="MutationApplier"/>'s, so a remote peer cannot bypass them.
/// A malformed inbound value throws <see cref="FormatException"/>, and the session drops it.
/// </summary>
public static partial class HubWire
{
    /// <summary>The colour a participant is shown in when the service sends none, or one that is not a hex colour.</summary>
    public const string FallbackColour = "#545a68";

    public static LayerMutation ToCore(LayerMutationDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return dto.Kind switch
        {
            "Upsert" => LayerMutation.Upsert(CanvasStateMapper.ToLayer(dto.Layer ?? throw Malformed("Upsert has no layer."))),
            "Delete" => LayerMutation.Delete(dto.Id ?? throw Malformed("Delete has no id.")),
            "Reorder" => LayerMutation.Reorder(dto.Order ?? throw Malformed("Reorder has no order.")),
            _ => throw Malformed($"'{dto.Kind}' is not a mutation kind."),
        };
    }

    public static LayerMutationDto ToWire(LayerMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        return mutation.Kind switch
        {
            LayerMutationKind.Upsert => new LayerMutationDto("Upsert", ToWire(mutation.Layer!), null, null),
            LayerMutationKind.Delete => new LayerMutationDto("Delete", null, mutation.Id, null),
            LayerMutationKind.Reorder => new LayerMutationDto("Reorder", null, null, mutation.Order),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation.Kind, "Unknown mutation kind."),
        };
    }

    public static LayerDto ToWire(Layer layer) => new(
        layer.Id,
        layer.Type.ToString(),
        layer.X,
        layer.Y,
        layer.Width,
        layer.Height,
        new FillDto(layer.Fill.R, layer.Fill.G, layer.Fill.B),
        layer.Value,
        layer.Points?.Select(point => (double[])[point.X, point.Y]).ToArray());

    /// <summary>The colour as the service sent it, if it is a six-digit hex colour; otherwise the fallback.</summary>
    public static string Colour(string? value) => value is not null && HexColour().IsMatch(value) ? value : FallbackColour;

    private static FormatException Malformed(string reason) => new(reason);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}
