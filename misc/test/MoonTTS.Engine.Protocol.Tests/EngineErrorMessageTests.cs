namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the defaults and JSON shape of the Error payload */
public sealed class EngineErrorMessageTests
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<EngineErrorMessage> TypeInfo =
        EngineProtocolJsonContext.Default.EngineErrorMessage;

    /* Code and message default to empty */
    [Fact]
    public void Properties_Default_AreEmpty()
    {
        var error = new EngineErrorMessage();

        Assert.Equal(string.Empty, error.Code);
        Assert.Equal(string.Empty, error.Message);
    }

    /* Properties are written in camelCase */
    [Fact]
    public void Serialize_Always_UsesCamelCaseNames()
    {
        var json = ProtocolJson.Serialize(new EngineErrorMessage { Code = "x", Message = "y" }, TypeInfo);

        Assert.Equal(["code", "message"], ProtocolJson.PropertyNames(json));
    }

    /* The specification's Error example deserializes into both fields */
    [Fact]
    public void Deserialize_SpecificationExample_ReadsAllFields()
    {
        var error = ProtocolJson.Deserialize("""{ "code": "model_not_found", "message": "No .onnx file was found." }""", TypeInfo);

        Assert.Equal("model_not_found", error.Code);
        Assert.Equal("No .onnx file was found.", error.Message);
    }
}
