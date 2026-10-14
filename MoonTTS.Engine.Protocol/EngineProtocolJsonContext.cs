using System.Text.Json.Serialization;

namespace MoonTTS.Engine.Protocol;

/* Source generator context for AOT serialization of every engine protocol payload and manifest */

/* IPC messages */
[JsonSerializable(typeof(EngineMetadataMessage))]
[JsonSerializable(typeof(LoadModelRequest))]
[JsonSerializable(typeof(VoicesResponse))]
[JsonSerializable(typeof(SynthesizeRequest))]
[JsonSerializable(typeof(EngineErrorMessage))]

/* Engine folder files */
[JsonSerializable(typeof(EngineManifest))]
[JsonSerializable(typeof(VoiceCatalogFile))]

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
public partial class EngineProtocolJsonContext : JsonSerializerContext
{
}
