using System.Text.Json;

namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the serializer options shared by every protocol payload and manifest */
public sealed class EngineProtocolJsonContextTests
{
    /* Every IPC message and engine-folder file has source-generated metadata (required for Native AOT) */
    [Theory]
    [InlineData(typeof(EngineMetadataMessage))]
    [InlineData(typeof(EngineAudioFormat))]
    [InlineData(typeof(LoadModelRequest))]
    [InlineData(typeof(VoicesResponse))]
    [InlineData(typeof(VoiceEntry))]
    [InlineData(typeof(SynthesizeRequest))]
    [InlineData(typeof(EngineErrorMessage))]
    [InlineData(typeof(EngineManifest))]
    [InlineData(typeof(EngineModelManifest))]
    [InlineData(typeof(VoiceCatalogFile))]
    public void GetTypeInfo_ProtocolType_IsRegistered(Type type)
    {
        Assert.NotNull(EngineProtocolJsonContext.Default.GetTypeInfo(type));
    }

    /* Property names are camel-cased */
    [Fact]
    public void Options_Always_UseCamelCaseNaming()
    {
        Assert.Same(JsonNamingPolicy.CamelCase, EngineProtocolJsonContext.Default.Options.PropertyNamingPolicy);
    }

    /* Output is indented so files written by the host stay readable */
    [Fact]
    public void Serialize_Always_IsIndented()
    {
        string json = JsonSerializer.Serialize(new EngineErrorMessage { Code = "c", Message = "m" }, EngineProtocolJsonContext.Default.EngineErrorMessage);

        Assert.Contains('\n', json);
    }

    /* Property names are matched regardless of case when reading */
    [Theory]
    [InlineData("""{"Code":"c","Message":"m"}""")]
    [InlineData("""{"CODE":"c","MESSAGE":"m"}""")]
    [InlineData("""{"code":"c","message":"m"}""")]
    public void Deserialize_AnyPropertyCase_IsMatched(string json)
    {
        var error = ProtocolJson.Deserialize(json, EngineProtocolJsonContext.Default.EngineErrorMessage);

        Assert.Equal("c", error.Code);
        Assert.Equal("m", error.Message);
    }

    /* Comments are skipped so hand-edited engine.json files load */
    [Fact]
    public void Deserialize_WithComments_SkipsComments()
    {
        const string json = """
            {
              // engine identity
              "id": "piper-tts", /* inline */
              "executable": "bin/piper-worker"
            }
            """;

        var manifest = ProtocolJson.Deserialize(json, EngineProtocolJsonContext.Default.EngineManifest);

        Assert.Equal("piper-tts", manifest.Id);
        Assert.Equal("bin/piper-worker", manifest.Executable);
    }

    /* Trailing commas are accepted in objects and arrays */
    [Fact]
    public void Deserialize_WithTrailingCommas_IsAccepted()
    {
        const string json = """{"modelId":"m","files":["a.onnx","b.bin",],}""";

        var request = ProtocolJson.Deserialize(json, EngineProtocolJsonContext.Default.LoadModelRequest);

        Assert.Equal("m", request.ModelId);
        Assert.Equal(["a.onnx", "b.bin"], request.Files);
    }

    /* Unknown properties are ignored so newer workers stay compatible */
    [Fact]
    public void Deserialize_UnknownProperty_IsIgnored()
    {
        var error = ProtocolJson.Deserialize("""{"code":"c","message":"m","extra":42}""", EngineProtocolJsonContext.Default.EngineErrorMessage);

        Assert.Equal("c", error.Code);
    }

    /* Null-valued properties are omitted when writing */
    [Fact]
    public void Serialize_NullProperty_IsOmitted()
    {
        var json = ProtocolJson.Serialize(new VoiceCatalogFile { Models = { ["m"] = [new VoiceEntry { Id = "v" }] } }, EngineProtocolJsonContext.Default.VoiceCatalogFile);

        Assert.Equal(["id"], ProtocolJson.PropertyNames(json.GetProperty("models").GetProperty("m")[0]));
    }
}
