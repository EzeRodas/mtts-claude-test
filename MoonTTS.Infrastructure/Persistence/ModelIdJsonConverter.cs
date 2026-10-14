using System.Text.Json;
using System.Text.Json.Serialization;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.Persistence;

/* File Structure:
        - Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) : ModelId
        - Write(Utf8JsonWriter writer, ModelId value, JsonSerializerOptions options) : void
*/

/* Persists ModelId as its "<engineId>/<modelName>" string.
   Settings written before engines were discovered dynamically stored the model as a number; those values
   (and any unparsable string) load as Unselected so the rest of settings.json is preserved */
public sealed class ModelIdJsonConverter : JsonConverter<ModelId>
{
    /* Reads a model id, mapping legacy or invalid values to Unselected */
    public override ModelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return ModelId.TryParse(reader.GetString(), out var modelId) ? modelId : ModelId.Unselected;
            case JsonTokenType.Null:
            case JsonTokenType.Number:
                return ModelId.Unselected;
            default:
                reader.Skip();
                return ModelId.Unselected;
        }
    }

    /* Writes the canonical string form, or null when no model is selected */
    public override void Write(Utf8JsonWriter writer, ModelId value, JsonSerializerOptions options)
    {
        if (value.IsUnselected)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.ToString());
    }
}
