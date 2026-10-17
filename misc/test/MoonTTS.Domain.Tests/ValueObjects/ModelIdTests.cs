using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Tests.ValueObjects;

/* Verifies construction, parsing, the unselected state, equality and formatting of ModelId */
public sealed class ModelIdTests
{
    /* The separator between engine id and model name is '/' */
    [Fact]
    public void Separator_Always_IsSlash()
    {
        Assert.Equal('/', ModelId.Separator);
    }

    /* The constructor stores both parts */
    [Fact]
    public void Constructor_ValidParts_StoresParts()
    {
        var id = new ModelId("kokoro", "v1.0-fp16");

        Assert.Equal("kokoro", id.EngineId);
        Assert.Equal("v1.0-fp16", id.ModelName);
    }

    /* A constructed id is not the unselected id */
    [Fact]
    public void Constructor_ValidParts_IsNotUnselected()
    {
        var id = new ModelId("kokoro", "v1.0-fp16");

        Assert.False(id.IsUnselected);
    }

    /* The model name may itself contain the separator; only the engine id may not */
    [Fact]
    public void Constructor_ModelNameWithSeparator_IsAccepted()
    {
        var id = new ModelId("kokoro", "v1/fp16");

        Assert.Equal("v1/fp16", id.ModelName);
    }

    /* A missing, blank or separator-containing engine id is rejected */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ko/koro")]
    [InlineData("/")]
    public void Constructor_InvalidEngineId_ThrowsArgumentException(string? engineId)
    {
        var exception = Assert.Throws<ArgumentException>(() => new ModelId(engineId!, "model"));

        Assert.Equal("engineId", exception.ParamName);
    }

    /* A missing or blank model name is rejected */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModelName_ThrowsArgumentException(string? modelName)
    {
        var exception = Assert.Throws<ArgumentException>(() => new ModelId("kokoro", modelName!));

        Assert.Equal("modelName", exception.ParamName);
    }

    /* Unselected reports IsUnselected */
    [Fact]
    public void Unselected_Always_IsUnselected()
    {
        Assert.True(ModelId.Unselected.IsUnselected);
    }

    /* Unselected exposes empty parts instead of null */
    [Fact]
    public void Unselected_Always_HasEmptyParts()
    {
        Assert.Equal(string.Empty, ModelId.Unselected.EngineId);
        Assert.Equal(string.Empty, ModelId.Unselected.ModelName);
    }

    /* Unselected is the default value of the struct */
    [Fact]
    public void Unselected_Always_EqualsDefault()
    {
        Assert.Equal(default(ModelId), ModelId.Unselected);
        Assert.True(default(ModelId).IsUnselected);
    }

    /* Blank input parses to Unselected and reports success */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_BlankInput_ReturnsTrueAndUnselected(string? value)
    {
        bool parsed = ModelId.TryParse(value, out var id);

        Assert.True(parsed);
        Assert.True(id.IsUnselected);
    }

    /* A well formed "<engine>/<model>" string parses into its two parts */
    [Fact]
    public void TryParse_WellFormed_ReturnsParts()
    {
        bool parsed = ModelId.TryParse("piper-tts/en_US-lessac-medium", out var id);

        Assert.True(parsed);
        Assert.Equal("piper-tts", id.EngineId);
        Assert.Equal("en_US-lessac-medium", id.ModelName);
    }

    /* Only the first separator splits; the rest belongs to the model name */
    [Fact]
    public void TryParse_SeveralSeparators_SplitsOnFirst()
    {
        bool parsed = ModelId.TryParse("kokoro/v1/fp16", out var id);

        Assert.True(parsed);
        Assert.Equal("kokoro", id.EngineId);
        Assert.Equal("v1/fp16", id.ModelName);
    }

    /* Malformed strings are rejected */
    [Theory]
    [InlineData("kokoro")]
    [InlineData("/model")]
    [InlineData("kokoro/")]
    [InlineData("/")]
    [InlineData(" /model")]
    [InlineData("kokoro/ ")]
    public void TryParse_Malformed_ReturnsFalse(string value)
    {
        Assert.False(ModelId.TryParse(value, out _));
    }

    /* A rejected string leaves the output at Unselected */
    [Theory]
    [InlineData("kokoro")]
    [InlineData("/model")]
    [InlineData("kokoro/ ")]
    public void TryParse_Malformed_OutputsUnselected(string value)
    {
        ModelId.TryParse(value, out var id);

        Assert.True(id.IsUnselected);
    }

    /* ToString returns the canonical "<engine>/<model>" form */
    [Fact]
    public void ToString_Selected_ReturnsCanonicalForm()
    {
        Assert.Equal("kokoro/v1.0-fp16", new ModelId("kokoro", "v1.0-fp16").ToString());
    }

    /* ToString of the unselected id is empty */
    [Fact]
    public void ToString_Unselected_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ModelId.Unselected.ToString());
    }

    /* Formatting and parsing again yields the same id */
    [Fact]
    public void ToString_ThenTryParse_RoundTrips()
    {
        var original = new ModelId("kokoro", "v1/fp16");

        ModelId.TryParse(original.ToString(), out var parsed);

        Assert.Equal(original, parsed);
    }

    /* Ids with the same parts are equal and share a hash code */
    [Fact]
    public void Equality_SameParts_AreEqual()
    {
        var a = new ModelId("kokoro", "model");
        var b = new ModelId("kokoro", "model");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /* Ids that differ in either part are not equal */
    [Theory]
    [InlineData("piper", "model")]
    [InlineData("kokoro", "other")]
    public void Equality_DifferentParts_AreNotEqual(string engineId, string modelName)
    {
        var a = new ModelId("kokoro", "model");
        var b = new ModelId(engineId, modelName);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    /* A selected id is never equal to Unselected */
    [Fact]
    public void Equality_SelectedVersusUnselected_AreNotEqual()
    {
        Assert.NotEqual(ModelId.Unselected, new ModelId("kokoro", "model"));
    }
}
