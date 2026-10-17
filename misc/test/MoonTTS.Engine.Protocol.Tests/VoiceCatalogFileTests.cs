namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the file name, defaults and JSON shape of voices.json */
public sealed class VoiceCatalogFileTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<VoiceCatalogFile> TypeInfo =
        EngineProtocolJsonContext.Default.VoiceCatalogFile;

    /* The catalog file is called voices.json */
    [Fact]
    public void FileName_Always_IsVoicesJson()
    {
        Assert.Equal("voices.json", VoiceCatalogFile.FileName);
    }

    /* A new catalog has no models */
    [Fact]
    public void Models_Default_IsEmpty()
    {
        Assert.Empty(new VoiceCatalogFile().Models);
    }

    /* The catalog is written as a camelCase "models" object keyed by verbatim model id */
    [Fact]
    public void Serialize_Always_WritesModelsKeyedByVerbatimId()
    {
        var catalog = new VoiceCatalogFile { Models = { ["en_US-Lessac"] = [new VoiceEntry { Id = "lessac" }] } };

        var json = ProtocolJson.Serialize(catalog, TypeInfo);

        Assert.Equal(["models"], ProtocolJson.PropertyNames(json));
        Assert.Equal(["en_US-Lessac"], ProtocolJson.PropertyNames(json.GetProperty("models")));
    }

    /* The specification's voices.json example deserializes into the voices of each model */
    [Fact]
    public void Deserialize_SpecificationExample_ReadsVoicesPerModel()
    {
        const string json = """
            {
              "models": {
                "en_US-lessac-medium": [
                  { "id": "lessac", "name": "Lessac", "language": "en-US" }
                ]
              }
            }
            """;

        var catalog = ProtocolJson.Deserialize(json, TypeInfo);

        var voice = Assert.Single(catalog.Models["en_US-lessac-medium"]);
        Assert.Equal("lessac", voice.Id);
        Assert.Equal("Lessac", voice.Name);
        Assert.Equal("en-US", voice.Language);
    }

    /* An empty object deserializes to an empty catalog */
    [Fact]
    public void Deserialize_EmptyObject_KeepsEmptyModels()
    {
        Assert.Empty(ProtocolJson.Deserialize("{}", TypeInfo).Models);
    }
}
