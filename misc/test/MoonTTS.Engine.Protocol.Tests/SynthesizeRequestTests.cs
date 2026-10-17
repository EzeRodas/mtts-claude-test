namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of the Synthesize payload */
public sealed class SynthesizeRequestTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<SynthesizeRequest> TypeInfo =
        EngineProtocolJsonContext.Default.SynthesizeRequest;

    /* Text and voice default to empty */
    [Fact]
    public void Strings_Default_AreEmpty()
    {
        var request = new SynthesizeRequest();

        Assert.Equal(string.Empty, request.Text);
        Assert.Equal(string.Empty, request.VoiceId);
    }

    /* Speed and pitch default to neutral (1.0) */
    [Fact]
    public void SpeedAndPitch_Default_AreOne()
    {
        var request = new SynthesizeRequest();

        Assert.Equal(1.0f, request.Speed);
        Assert.Equal(1.0f, request.Pitch);
    }

    /* Properties are written in camelCase */
    [Fact]
    public void Serialize_Always_UsesCamelCaseNames()
    {
        var json = ProtocolJson.Serialize(new SynthesizeRequest { Text = "Hi", VoiceId = "af_heart" }, TypeInfo);

        Assert.Equal(["text", "voiceId", "speed", "pitch"], ProtocolJson.PropertyNames(json));
    }

    /* The specification's Synthesize example deserializes into every field */
    [Fact]
    public void Deserialize_SpecificationExample_ReadsAllFields()
    {
        const string json = """
            {
              "text": "Hello world, this is accessibility speech synthesis.",
              "voiceId": "af_heart",
              "speed": 1.25,
              "pitch": 0.75
            }
            """;

        var request = ProtocolJson.Deserialize(json, TypeInfo);

        Assert.Equal("Hello world, this is accessibility speech synthesis.", request.Text);
        Assert.Equal("af_heart", request.VoiceId);
        Assert.Equal(1.25f, request.Speed);
        Assert.Equal(0.75f, request.Pitch);
    }

    /* Omitted speed and pitch keep their neutral defaults */
    [Fact]
    public void Deserialize_MissingSpeedAndPitch_KeepsDefaults()
    {
        var request = ProtocolJson.Deserialize("""{"text":"Hi","voiceId":"af_heart"}""", TypeInfo);

        Assert.Equal(1.0f, request.Speed);
        Assert.Equal(1.0f, request.Pitch);
    }
}
