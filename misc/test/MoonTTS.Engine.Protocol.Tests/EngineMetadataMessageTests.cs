namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of the MetadataResponse payload */
public sealed class EngineMetadataMessageTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<EngineMetadataMessage> TypeInfo =
        EngineProtocolJsonContext.Default.EngineMetadataMessage;

    /* Identity strings default to empty and the author to null */
    [Fact]
    public void Identity_Default_IsEmptyWithoutAuthor()
    {
        var message = new EngineMetadataMessage();

        Assert.Equal(string.Empty, message.EngineId);
        Assert.Equal(string.Empty, message.EngineName);
        Assert.Equal(string.Empty, message.Version);
        Assert.Null(message.Author);
    }

    /* A new message carries its own audio format and no capabilities */
    [Fact]
    public void Capabilities_Default_AreFalseWithAudioFormat()
    {
        var message = new EngineMetadataMessage();

        Assert.NotNull(message.AudioFormat);
        Assert.False(message.SupportsStreaming);
        Assert.False(message.SupportsSpeedAdjustment);
        Assert.False(message.SupportsPitchAdjustment);
    }

    /* Properties are written in camelCase */
    [Fact]
    public void Serialize_FullMessage_UsesCamelCaseNames()
    {
        var json = ProtocolJson.Serialize(new EngineMetadataMessage { Author = "Moon-TTS Team" }, TypeInfo);

        Assert.Equal(
            ["engineId", "engineName", "version", "author", "audioFormat", "supportsStreaming", "supportsSpeedAdjustment", "supportsPitchAdjustment"],
            ProtocolJson.PropertyNames(json));
    }

    /* A null author is omitted from the JSON */
    [Fact]
    public void Serialize_NullAuthor_OmitsAuthor()
    {
        var json = ProtocolJson.Serialize(new EngineMetadataMessage(), TypeInfo);

        Assert.False(json.TryGetProperty("author", out _));
    }

    /* The specification's MetadataResponse example deserializes into every field */
    [Fact]
    public void Deserialize_SpecificationExample_ReadsAllFields()
    {
        const string json = """
            {
              "engineId": "kokoro-onnx",
              "engineName": "Kokoro ONNX Neural Synthesizer",
              "version": "1.0.0",
              "author": "Moon-TTS Team",
              "audioFormat": { "sampleRate": 24000, "channels": 1, "bitDepth": 16, "encoding": "PCM_SIGNED" },
              "supportsStreaming": true,
              "supportsPitchAdjustment": false,
              "supportsSpeedAdjustment": true
            }
            """;

        var message = ProtocolJson.Deserialize(json, TypeInfo);

        Assert.Equal("kokoro-onnx", message.EngineId);
        Assert.Equal("Kokoro ONNX Neural Synthesizer", message.EngineName);
        Assert.Equal("1.0.0", message.Version);
        Assert.Equal("Moon-TTS Team", message.Author);
        Assert.Equal(24000, message.AudioFormat.SampleRate);
        Assert.Equal((short)1, message.AudioFormat.Channels);
        Assert.Equal((short)16, message.AudioFormat.BitDepth);
        Assert.True(message.SupportsStreaming);
        Assert.False(message.SupportsPitchAdjustment);
        Assert.True(message.SupportsSpeedAdjustment);
    }

    /* The minimal metadata of the Python template (no author, encoding or capabilities) still deserializes */
    [Fact]
    public void Deserialize_MinimalPythonTemplate_UsesDefaults()
    {
        const string json = """{"engineId": "python-tts", "engineName": "Python TTS", "version": "1.0.0", "audioFormat": {"sampleRate": 22050, "channels": 1, "bitDepth": 16}}""";

        var message = ProtocolJson.Deserialize(json, TypeInfo);

        Assert.Equal("python-tts", message.EngineId);
        Assert.Null(message.Author);
        Assert.Equal("PCM_SIGNED", message.AudioFormat.Encoding);
        Assert.False(message.SupportsStreaming);
    }
}
