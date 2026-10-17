namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of the VoicesResponse payload */
public sealed class VoicesResponseTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<VoicesResponse> TypeInfo =
        EngineProtocolJsonContext.Default.VoicesResponse;

    /* A new response lists no voices */
    [Fact]
    public void Voices_Default_IsEmpty()
    {
        Assert.Empty(new VoicesResponse().Voices);
    }

    /* The voice list is written as a camelCase "voices" array */
    [Fact]
    public void Serialize_Always_WritesVoicesArray()
    {
        var json = ProtocolJson.Serialize(new VoicesResponse { Voices = [new VoiceEntry { Id = "af_heart" }] }, TypeInfo);

        Assert.Equal(["voices"], ProtocolJson.PropertyNames(json));
        Assert.Equal("af_heart", json.GetProperty("voices")[0].GetProperty("id").GetString());
    }

    /* The specification's VoicesResponse example deserializes into every voice */
    [Fact]
    public void Deserialize_SpecificationExample_ReadsAllVoices()
    {
        const string json = """
            {
              "voices": [
                { "id": "af_heart", "name": "Heart", "language": "en-US" },
                { "id": "bm_george", "name": "George", "language": "en-GB" }
              ]
            }
            """;

        var response = ProtocolJson.Deserialize(json, TypeInfo);

        Assert.Equal(2, response.Voices.Count);
        Assert.Equal("af_heart", response.Voices[0].Id);
        Assert.Equal("Heart", response.Voices[0].Name);
        Assert.Equal("en-US", response.Voices[0].Language);
        Assert.Equal("bm_george", response.Voices[1].Id);
        Assert.Equal("en-GB", response.Voices[1].Language);
    }

    /* A response without a voices property deserializes to an empty list */
    [Fact]
    public void Deserialize_MissingVoices_KeepsEmptyList()
    {
        var response = ProtocolJson.Deserialize("{}", TypeInfo);

        Assert.Empty(response.Voices);
    }
}
