using MoonTTS.Core.Utilities;

namespace MoonTTS.Core.Tests.Utilities;

/* Tests for PathSanitizer.SanitizeFolderName */
public sealed class PathSanitizerTests
{
    /* Null, empty and whitespace-only names fall back to the default folder name */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeFolderName_BlankName_ReturnsDefaultFallback(string? rawName)
    {
        string result = PathSanitizer.SanitizeFolderName(rawName!);

        Assert.Equal(PathSanitizer.DefaultFallbackName, result);
    }

    /* A name made of valid characters is returned unchanged */
    [Fact]
    public void SanitizeFolderName_ValidName_ReturnsSameName()
    {
        string result = PathSanitizer.SanitizeFolderName("Gaming Profile 2");

        Assert.Equal("Gaming Profile 2", result);
    }

    /* Non-ASCII letters are valid folder characters and are kept */
    [Fact]
    public void SanitizeFolderName_UnicodeLetters_AreKept()
    {
        string result = PathSanitizer.SanitizeFolderName("Perfil Música");

        Assert.Equal("Perfil Música", result);
    }

    /* Surrounding whitespace is trimmed */
    [Fact]
    public void SanitizeFolderName_SurroundingWhitespace_IsTrimmed()
    {
        string result = PathSanitizer.SanitizeFolderName("  Work  ");

        Assert.Equal("Work", result);
    }

    /* Every character restricted by some OS is replaced by an underscore */
    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a\0b")]
    public void SanitizeFolderName_InvalidCharacter_IsReplacedByUnderscore(string rawName)
    {
        string result = PathSanitizer.SanitizeFolderName(rawName);

        Assert.Equal("a_b", result);
    }

    /* Leading and trailing dots, spaces and underscores are trimmed from the result */
    [Fact]
    public void SanitizeFolderName_EdgeDotsAndUnderscores_AreTrimmed()
    {
        string result = PathSanitizer.SanitizeFolderName("._name_.");

        Assert.Equal("name", result);
    }

    /* A name whose characters are all invalid collapses to nothing and falls back to the default */
    [Fact]
    public void SanitizeFolderName_OnlyInvalidCharacters_ReturnsDefaultFallback()
    {
        string result = PathSanitizer.SanitizeFolderName("<>:*?|");

        Assert.Equal(PathSanitizer.DefaultFallbackName, result);
    }

    /* Windows reserved device names get a "_profile" suffix, ignoring case */
    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Com1")]
    [InlineData("LPT9")]
    [InlineData("aux")]
    [InlineData("PRN")]
    public void SanitizeFolderName_ReservedWindowsName_AppendsProfileSuffix(string rawName)
    {
        string result = PathSanitizer.SanitizeFolderName(rawName);

        Assert.Equal($"{rawName}_profile", result);
    }

    /* Reserved names are detected after trimming, so padding does not bypass the check */
    [Fact]
    public void SanitizeFolderName_PaddedReservedName_AppendsProfileSuffix()
    {
        string result = PathSanitizer.SanitizeFolderName(" con. ");

        Assert.Equal("con_profile", result);
    }

    /* A reserved name used only as a prefix is not reserved */
    [Fact]
    public void SanitizeFolderName_ReservedNamePrefix_IsNotChanged()
    {
        string result = PathSanitizer.SanitizeFolderName("Console");

        Assert.Equal("Console", result);
    }
}
