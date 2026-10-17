using MoonTTS.Core.Utilities;

namespace MoonTTS.Core.Tests.Utilities;

/* Tests for TextSplitter.SplitIntoSentences */
public sealed class TextSplitterTests
{
    /* Null, empty and whitespace-only input produce no sentences */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\n ")]
    public void SplitIntoSentences_BlankText_ReturnsEmptyList(string? text)
    {
        var sentences = TextSplitter.SplitIntoSentences(text!);

        Assert.Empty(sentences);
    }

    /* Text without terminators is returned as a single trimmed sentence */
    [Fact]
    public void SplitIntoSentences_NoPunctuation_ReturnsSingleTrimmedSentence()
    {
        var sentences = TextSplitter.SplitIntoSentences("  hello world  ");

        Assert.Equal(["hello world"], sentences);
    }

    /* Each of . ! ? ; ends a sentence and stays attached to it */
    [Fact]
    public void SplitIntoSentences_EachTerminator_SplitsAndKeepsPunctuation()
    {
        var sentences = TextSplitter.SplitIntoSentences("One. Two! Three? Four; Five");

        Assert.Equal(["One.", "Two!", "Three?", "Four;", "Five"], sentences);
    }

    /* Line breaks end a sentence and are trimmed away */
    [Fact]
    public void SplitIntoSentences_LineBreaks_SplitAndAreTrimmed()
    {
        var sentences = TextSplitter.SplitIntoSentences("First line\r\nSecond line\nThird");

        Assert.Equal(["First line", "Second line", "Third"], sentences);
    }

    /* A run of terminators stays together with the sentence it ends */
    [Fact]
    public void SplitIntoSentences_ConsecutiveTerminators_StayWithTheirSentence()
    {
        var sentences = TextSplitter.SplitIntoSentences("Wait... What?! Fine.");

        Assert.Equal(["Wait...", "What?!", "Fine."], sentences);
    }

    /* Text made only of terminators has no sentence body and is returned whole (trimmed) */
    [Fact]
    public void SplitIntoSentences_OnlyPunctuation_ReturnsWholeTrimmedText()
    {
        var sentences = TextSplitter.SplitIntoSentences(" ...!? ");

        Assert.Equal(["...!?"], sentences);
    }

    /* A whitespace-only fragment after the last terminator is dropped instead of becoming an empty sentence */
    [Fact]
    public void SplitIntoSentences_TrailingWhitespaceFragment_IsDropped()
    {
        var sentences = TextSplitter.SplitIntoSentences("Hi. Bye.   ");

        Assert.Equal(["Hi.", "Bye."], sentences);
    }
}
