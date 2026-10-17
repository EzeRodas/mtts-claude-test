namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of the LoadModel payload */
public sealed class LoadModelRequestTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<LoadModelRequest> TypeInfo =
        EngineProtocolJsonContext.Default.LoadModelRequest;

    /* A new request has empty strings and no files */
    [Fact]
    public void Properties_Default_AreEmpty()
    {
        var request = new LoadModelRequest();

        Assert.Equal(string.Empty, request.ModelId);
        Assert.Equal(string.Empty, request.ModelDirectory);
        Assert.Empty(request.Files);
    }

    /* Properties are written in camelCase and the file list as an array */
    [Fact]
    public void Serialize_Always_UsesCamelCaseNamesAndArray()
    {
        var request = new LoadModelRequest { ModelId = "kokoro-v1.0-fp16", ModelDirectory = "/models/kokoro", Files = ["kokoro.onnx"] };

        var json = ProtocolJson.Serialize(request, TypeInfo);

        Assert.Equal(["modelId", "modelDirectory", "files"], ProtocolJson.PropertyNames(json));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, json.GetProperty("files").ValueKind);
        Assert.Equal("kokoro.onnx", json.GetProperty("files")[0].GetString());
    }

    /* The specification's LoadModel example deserializes into every field */
    [Fact]
    public void Deserialize_SpecificationExample_ReadsAllFields()
    {
        const string json = """
            {
              "modelId": "kokoro-v1.0-fp16",
              "modelDirectory": "/home/user/.local/share/Moon-TTS/models/kokoro",
              "files": ["kokoro-v1.0-fp16.onnx"]
            }
            """;

        var request = ProtocolJson.Deserialize(json, TypeInfo);

        Assert.Equal("kokoro-v1.0-fp16", request.ModelId);
        Assert.Equal("/home/user/.local/share/Moon-TTS/models/kokoro", request.ModelDirectory);
        Assert.Equal(["kokoro-v1.0-fp16.onnx"], request.Files);
    }
}
