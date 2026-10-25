using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiroClone.CanvasService.Canvas;

/// <summary>
/// The one JSON shape for canvas data: REST responses, hub payloads and the stored snapshot. camelCase keys, enums by
/// name, absent optional fields left out. Shared so the three can never drift apart.
/// </summary>
public static class CanvasJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
