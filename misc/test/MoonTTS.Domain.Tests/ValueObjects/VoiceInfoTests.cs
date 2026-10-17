using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Tests.ValueObjects;

/* Verifies the display label and value semantics of VoiceInfo */
public sealed class VoiceInfoTests
{
    /* Name and Language are optional and default to null */
    [Fact]
    public void Constructor_IdOnly_LeavesNameAndLanguageNull()
    {
        var voice = new VoiceInfo("af_heart");

        Assert.Equal("af_heart", voice.Id);
        Assert.Null(voice.Name);
        Assert.Null(voice.Language);
    }

    /* Without a usable name the label is just the id, whatever the language */
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, "en-US")]
    [InlineData(" ", "en-US")]
    public void DisplayName_NoName_ReturnsId(string? name, string? language)
    {
        var voice = new VoiceInfo("af_heart", name, language);

        Assert.Equal("af_heart", voice.DisplayName);
    }

    /* With a name but no usable language the label is "Name (Id)" */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayName_NameWithoutLanguage_ReturnsNameAndId(string? language)
    {
        var voice = new VoiceInfo("af_heart", "Heart", language);

        Assert.Equal("Heart (af_heart)", voice.DisplayName);
    }

    /* With a name and a language the label is "Name (Id, Language)" */
    [Fact]
    public void DisplayName_NameAndLanguage_ReturnsNameIdAndLanguage()
    {
        var voice = new VoiceInfo("af_heart", "Heart", "en-US");

        Assert.Equal("Heart (af_heart, en-US)", voice.DisplayName);
    }

    /* ToString renders the same label as DisplayName */
    [Theory]
    [InlineData(null, null)]
    [InlineData("Heart", null)]
    [InlineData("Heart", "en-US")]
    public void ToString_Always_ReturnsDisplayName(string? name, string? language)
    {
        var voice = new VoiceInfo("af_heart", name, language);

        Assert.Equal(voice.DisplayName, voice.ToString());
    }

    /* Voices with the same id, name and language are equal */
    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        Assert.Equal(new VoiceInfo("af_heart", "Heart", "en-US"), new VoiceInfo("af_heart", "Heart", "en-US"));
    }

    /* Voices that differ in any field are not equal */
    [Theory]
    [InlineData("bm_george", "Heart", "en-US")]
    [InlineData("af_heart", "George", "en-US")]
    [InlineData("af_heart", "Heart", "en-GB")]
    public void Equality_DifferentValues_AreNotEqual(string id, string name, string language)
    {
        Assert.NotEqual(new VoiceInfo("af_heart", "Heart", "en-US"), new VoiceInfo(id, name, language));
    }
}
