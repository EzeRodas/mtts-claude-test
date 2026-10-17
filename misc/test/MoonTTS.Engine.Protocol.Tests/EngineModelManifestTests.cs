namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of a model entry inside engine.json */
public sealed class EngineModelManifestTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<EngineManifest> TypeInfo =
        EngineProtocolJsonContext.Default.EngineManifest;

    /* A new model has empty strings and no download files */
    [Fact]
    public void Properties_Default_AreEmpty()
    {
        var model = new EngineModelManifest();

        Assert.Equal(string.Empty, model.Id);
        Assert.Equal(string.Empty, model.DisplayName);
        Assert.Empty(model.DownloadFiles);
    }

    /* A model is written with camelCase names */
    [Fact]
    public void Serialize_Always_UsesCamelCaseNames()
    {
        var manifest = new EngineManifest { Models = [new EngineModelManifest { Id = "m", DisplayName = "M" }] };

        var model = ProtocolJson.Serialize(manifest, TypeInfo).GetProperty("models")[0];

        Assert.Equal(["id", "displayName", "downloadFiles"], ProtocolJson.PropertyNames(model));
    }

    /* File names used as dictionary keys are written verbatim, not camel-cased */
    [Fact]
    public void Serialize_DownloadFiles_KeepsKeysVerbatim()
    {
        var manifest = new EngineManifest
        {
            Models = [new EngineModelManifest { Id = "m", DownloadFiles = new() { ["Model-FP16.onnx"] = "https://example.org/Model-FP16.onnx" } }]
        };

        var files = ProtocolJson.Serialize(manifest, TypeInfo).GetProperty("models")[0].GetProperty("downloadFiles");

        Assert.Equal(["Model-FP16.onnx"], ProtocolJson.PropertyNames(files));
        Assert.Equal("https://example.org/Model-FP16.onnx", files.GetProperty("Model-FP16.onnx").GetString());
    }

    /* Download files deserialize as local-name to URL pairs */
    [Fact]
    public void Deserialize_DownloadFiles_MapsNameToUrl()
    {
        const string json = """{"models":[{"id":"m","displayName":"M","downloadFiles":{"Voices.bin":"https://example.org/voices.bin"}}]}""";

        var model = ProtocolJson.Deserialize(json, TypeInfo).Models[0];

        Assert.Equal("https://example.org/voices.bin", model.DownloadFiles["Voices.bin"]);
    }

    /* A model without download files (e.g. a cloud voice) deserializes with an empty map */
    [Fact]
    public void Deserialize_MissingDownloadFiles_KeepsEmptyMap()
    {
        var model = ProtocolJson.Deserialize("""{"models":[{"id":"cloud","displayName":"Cloud"}]}""", TypeInfo).Models[0];

        Assert.Empty(model.DownloadFiles);
    }
}
