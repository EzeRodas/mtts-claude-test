using System.Text.Json;

namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of a voice descriptor */
public sealed class VoiceEntryTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<VoicesResponse> ResponseTypeInfo =
        EngineProtocolJsonContext.Default.VoicesResponse;

    /* A new voice has an empty id and no name or language */
    [Fact]
    public void Properties_Default_EmptyIdWithoutOptionalFields()
    {
        var voice = new VoiceEntry();

        Assert.Equal(string.Empty, voice.Id);
        Assert.Null(voice.Name);
        Assert.Null(voice.Language);
    }

    /* A fully described voice is written with camelCase names */
    [Fact]
    public void Serialize_AllFields_UsesCamelCaseNames()
    {
        var response = new VoicesResponse { Voices = [new VoiceEntry { Id = "af_heart", Name = "Heart", Language = "en-US" }] };

        var voice = ProtocolJson.Serialize(response, ResponseTypeInfo).GetProperty("voices")[0];

        Assert.Equal(["id", "name", "language"], ProtocolJson.PropertyNames(voice));
    }

    /* Null name and language are omitted, leaving only the id */
    [Fact]
    public void Serialize_IdOnly_OmitsNullFields()
    {
        var response = new VoicesResponse { Voices = [new VoiceEntry { Id = "af_heart" }] };

        var voice = ProtocolJson.Serialize(response, ResponseTypeInfo).GetProperty("voices")[0];

        Assert.Equal(["id"], ProtocolJson.PropertyNames(voice));
    }

    /* A voice with only an id deserializes with null optional fields */
    [Fact]
    public void Deserialize_IdOnly_LeavesOptionalFieldsNull()
    {
        var response = ProtocolJson.Deserialize("""{"voices":[{"id":"lessac"}]}""", ResponseTypeInfo);

        Assert.Equal("lessac", response.Voices[0].Id);
        Assert.Null(response.Voices[0].Name);
        Assert.Null(response.Voices[0].Language);
    }

    /* An explicit JSON null for an optional field deserializes to null */
    [Fact]
    public void Deserialize_ExplicitNulls_AreNull()
    {
        var response = ProtocolJson.Deserialize("""{"voices":[{"id":"lessac","name":null,"language":null}]}""", ResponseTypeInfo);

        Assert.Null(response.Voices[0].Name);
        Assert.Null(response.Voices[0].Language);
    }

    /* A voice array element of the wrong JSON kind is rejected */
    [Fact]
    public void Deserialize_NonObjectVoice_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => ProtocolJson.Deserialize("""{"voices":["af_heart"]}""", ResponseTypeInfo));
    }
}
