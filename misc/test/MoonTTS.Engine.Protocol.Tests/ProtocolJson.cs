using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MoonTTS.Engine.Protocol.Tests;

/* File Structure:
        - Serialize<T>(T value, JsonTypeInfo<T> typeInfo) : JsonElement
        - Deserialize<T>(string json, JsonTypeInfo<T> typeInfo) : T
        - PropertyNames(JsonElement element) : string[]
*/

/* JSON helpers for asserting the wire shape of protocol payloads */
internal static class ProtocolJson
{
    /* Serializes through the source-generated metadata and returns the parsed root element */
    public static JsonElement Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
        return document.RootElement.Clone();
    }

    /* Deserializes through the source-generated metadata, failing the test on a null result */
    public static T Deserialize<T>(string json, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(json, typeInfo) ?? throw new InvalidOperationException("JSON deserialized to null.");

    /* Names of the properties of a JSON object, in document order */
    public static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToArray();
}
