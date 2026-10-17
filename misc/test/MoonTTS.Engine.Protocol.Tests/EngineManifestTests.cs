namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the file name, defaults and JSON shape of engine.json */
public sealed class EngineManifestTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<EngineManifest> TypeInfo =
        EngineProtocolJsonContext.Default.EngineManifest;

    /* The manifest file is called engine.json */
    [Fact]
    public void FileName_Always_IsEngineJson()
    {
        Assert.Equal("engine.json", EngineManifest.FileName);
    }

    /* Required strings default to empty and optional ones to null */
    [Fact]
    public void Properties_Default_AreEmptyOrNull()
    {
        var manifest = new EngineManifest();

        Assert.Equal(string.Empty, manifest.Id);
        Assert.Equal(string.Empty, manifest.DisplayName);
        Assert.Equal(string.Empty, manifest.Executable);
        Assert.Null(manifest.Description);
        Assert.Null(manifest.Version);
    }

    /* A new manifest lists no models */
    [Fact]
    public void Models_Default_IsEmpty()
    {
        Assert.Empty(new EngineManifest().Models);
    }

    /* A complete manifest is written with camelCase names */
    [Fact]
    public void Serialize_AllFields_UsesCamelCaseNames()
    {
        var manifest = new EngineManifest { Id = "piper-tts", DisplayName = "Piper", Description = "d", Version = "1.2.0", Executable = "bin/piper-worker" };

        var json = ProtocolJson.Serialize(manifest, TypeInfo);

        Assert.Equal(["id", "displayName", "description", "version", "executable", "models"], ProtocolJson.PropertyNames(json));
    }

    /* Null description and version are omitted */
    [Fact]
    public void Serialize_NullOptionalFields_AreOmitted()
    {
        var json = ProtocolJson.Serialize(new EngineManifest { Id = "piper-tts" }, TypeInfo);

        Assert.False(json.TryGetProperty("description", out _));
        Assert.False(json.TryGetProperty("version", out _));
    }

    /* The specification's engine.json example deserializes into every field, including its models */
    [Fact]
    public void Deserialize_SpecificationExample_ReadsAllFields()
    {
        const string json = """
            {
              "id": "piper-tts",
              "displayName": "Piper Neural Synthesizer",
              "description": "Ultra-fast local neural text-to-speech engine running on ONNX.",
              "version": "1.2.0",
              "executable": "bin/piper-worker",
              "models": [
                {
                  "id": "en_US-lessac-medium",
                  "displayName": "Lessac (US English, Medium)",
                  "downloadFiles": {
                    "en_US-lessac-medium.onnx": "https://example.org/en_US-lessac-medium.onnx",
                    "en_US-lessac-medium.onnx.json": "https://example.org/en_US-lessac-medium.onnx.json"
                  }
                }
              ]
            }
            """;

        var manifest = ProtocolJson.Deserialize(json, TypeInfo);

        Assert.Equal("piper-tts", manifest.Id);
        Assert.Equal("Piper Neural Synthesizer", manifest.DisplayName);
        Assert.Equal("Ultra-fast local neural text-to-speech engine running on ONNX.", manifest.Description);
        Assert.Equal("1.2.0", manifest.Version);
        Assert.Equal("bin/piper-worker", manifest.Executable);
        var model = Assert.Single(manifest.Models);
        Assert.Equal("en_US-lessac-medium", model.Id);
        Assert.Equal(2, model.DownloadFiles.Count);
    }

    /* A manifest without optional fields or models deserializes with defaults */
    [Fact]
    public void Deserialize_MinimalManifest_UsesDefaults()
    {
        var manifest = ProtocolJson.Deserialize("""{"id":"dummy","displayName":"Dummy","executable":"worker"}""", TypeInfo);

        Assert.Null(manifest.Description);
        Assert.Null(manifest.Version);
        Assert.Empty(manifest.Models);
    }
}
