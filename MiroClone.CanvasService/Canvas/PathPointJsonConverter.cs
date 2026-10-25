using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiroClone.CanvasService.Canvas;

/// <summary>Reads and writes a path vertex as the two-element array the contract uses: <c>[x, y]</c>.</summary>
public sealed class PathPointJsonConverter : JsonConverter<PathPoint>
{
    public override PathPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("A path point must be a two-element array.");
        }

        reader.Read();
        var x = reader.GetDouble();
        reader.Read();
        var y = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("A path point must be a two-element array.");
        }

        return new PathPoint(x, y);
    }

    public override void Write(Utf8JsonWriter writer, PathPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}
