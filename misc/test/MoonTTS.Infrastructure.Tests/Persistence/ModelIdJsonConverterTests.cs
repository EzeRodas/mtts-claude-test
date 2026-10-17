using System.Text;
using System.Text.Json;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.Infrastructure.Tests.Persistence;

/* ModelIdJsonConverter reading every JSON token kind and writing the canonical string form */
public sealed class ModelIdJsonConverterTests
{
    private static readonly ModelIdJsonConverter Converter = new();

    /* Runs the converter on a single JSON value */
    private static ModelId Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return Converter.Read(ref reader, typeof(ModelId), JsonSerializerOptions.Default);
    }

    /* Runs the converter's writer and returns the produced JSON */
    private static string Write(ModelId value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            Converter.Write(writer, value, JsonSerializerOptions.Default);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /* A "<engine>/<model>" string reads as that model */
    [Fact]
    public void Read_ValidString_ReturnsModelId()
    {
        Assert.Equal(new ModelId("kokoro", "v1.0-fp16"), Read("\"kokoro/v1.0-fp16\""));
    }

    /* Strings that are not "<engine>/<model>" read as Unselected */
    [Theory]
    [InlineData("\"no-separator\"")]
    [InlineData("\"/model\"")]
    [InlineData("\"engine/\"")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Read_InvalidString_ReturnsUnselected(string json)
    {
        Assert.True(Read(json).IsUnselected);
    }

    /* JSON null reads as Unselected */
    [Fact]
    public void Read_Null_ReturnsUnselected()
    {
        Assert.True(Read("null").IsUnselected);
    }

    /* Legacy numeric model values read as Unselected */
    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("-1.5")]
    public void Read_LegacyNumber_ReturnsUnselected(string json)
    {
        Assert.True(Read(json).IsUnselected);
    }

    /* Other token kinds read as Unselected */
    [Theory]
    [InlineData("true")]
    [InlineData("{ \"engine\": \"kokoro\" }")]
    [InlineData("[ \"kokoro\", \"v1\" ]")]
    public void Read_UnexpectedToken_ReturnsUnselected(string json)
    {
        Assert.True(Read(json).IsUnselected);
    }

    /* An object or array value is skipped entirely, so the properties after it still load */
    [Theory]
    [InlineData("{ \"nested\": { \"deep\": [1, 2] } }")]
    [InlineData("[ 1, [ 2, 3 ], { \"a\": 4 } ]")]
    public void Read_ComplexValueInsideSettings_KeepsFollowingProperties(string garbage)
    {
        string json = $"{{ \"ActiveModel\": {garbage}, \"PrimaryVolume\": 42 }}";

        var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig)!;

        Assert.True(config.ActiveModel.IsUnselected);
        Assert.Equal(42, config.PrimaryVolume);
    }

    /* A legacy numeric ActiveModel keeps the rest of settings.json */
    [Fact]
    public void Read_LegacyNumberInsideSettings_KeepsFollowingProperties()
    {
        var config = JsonSerializer.Deserialize("""{ "ActiveModel": 2, "MonitorVolume": 55 }""", AppJsonContext.Default.AppConfig)!;

        Assert.True(config.ActiveModel.IsUnselected);
        Assert.Equal(55, config.MonitorVolume);
    }

    /* A selected model is written as its canonical string */
    [Fact]
    public void Write_SelectedModel_WritesString()
    {
        Assert.Equal("\"piper/en_US-lessac\"", Write(new ModelId("piper", "en_US-lessac")));
    }

    /* Unselected is written as null */
    [Fact]
    public void Write_Unselected_WritesNull()
    {
        Assert.Equal("null", Write(ModelId.Unselected));
    }

    /* Written values read back unchanged */
    [Fact]
    public void Write_ThenRead_RoundTrips()
    {
        var model = new ModelId("kokoro", "v1.1-zh");

        Assert.Equal(model, Read(Write(model)));
    }

    /* The converter is used for ModelId properties of persisted entities */
    [Fact]
    public void Write_AppConfigActiveModel_UsesStringForm()
    {
        string json = JsonSerializer.Serialize(new AppConfig { ActiveModel = new ModelId("kokoro", "v1") }, AppJsonContext.Default.AppConfig);

        Assert.Contains("\"ActiveModel\": \"kokoro/v1\"", json);
    }
}
