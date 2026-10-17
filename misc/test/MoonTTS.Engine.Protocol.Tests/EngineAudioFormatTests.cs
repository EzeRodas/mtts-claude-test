namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of the audio format inside MetadataResponse */
public sealed class EngineAudioFormatTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<EngineAudioFormat> TypeInfo =
        EngineProtocolJsonContext.Default.EngineAudioFormat;

    /* The numeric fields default to zero (no format declared) */
    [Fact]
    public void Numbers_Default_AreZero()
    {
        var format = new EngineAudioFormat();

        Assert.Equal(0, format.SampleRate);
        Assert.Equal((short)0, format.Channels);
        Assert.Equal((short)0, format.BitDepth);
    }

    /* The encoding defaults to signed PCM */
    [Fact]
    public void Encoding_Default_IsPcmSigned()
    {
        Assert.Equal("PCM_SIGNED", new EngineAudioFormat().Encoding);
    }

    /* Properties are written in camelCase */
    [Fact]
    public void Serialize_Always_UsesCamelCaseNames()
    {
        var json = ProtocolJson.Serialize(new EngineAudioFormat { SampleRate = 24000, Channels = 1, BitDepth = 16 }, TypeInfo);

        Assert.Equal(["sampleRate", "channels", "bitDepth", "encoding"], ProtocolJson.PropertyNames(json));
    }

    /* Serialized values keep their numbers and encoding */
    [Fact]
    public void Serialize_Always_WritesValues()
    {
        var json = ProtocolJson.Serialize(new EngineAudioFormat { SampleRate = 24000, Channels = 2, BitDepth = 16 }, TypeInfo);

        Assert.Equal(24000, json.GetProperty("sampleRate").GetInt32());
        Assert.Equal(2, json.GetProperty("channels").GetInt16());
        Assert.Equal(16, json.GetProperty("bitDepth").GetInt16());
        Assert.Equal("PCM_SIGNED", json.GetProperty("encoding").GetString());
    }
}
