using MoonTTS.CLI.Tests.Support;
using MoonTTS.CLI.Utilities;

namespace MoonTTS.CLI.Tests.Utilities;

/* Terminal writes straight to Console; each test captures it with CliConsoleCapture. Tables assume the width used
   when output is redirected (120 columns, as under `dotnet test`), except where the layout is width independent */
[Collection(TestCollections.Environment)]
public sealed class TerminalTests
{
    private static readonly string NewLine = Environment.NewLine;

    /* Runs `write` with the console captured and returns what it printed */
    private static string Capture(Action write)
    {
        using var console = new CliConsoleCapture();
        write();
        return console.Output;
    }

    /* Runs a prompt with `stdin` typed by the user and returns its answer and output */
    private static (bool Answer, string Output) ConfirmWith(string stdin, bool defaultYes = false)
    {
        using var console = new CliConsoleCapture(stdin);
        bool answer = Terminal.Confirm("Continue?", defaultYes);
        return (answer, console.Output);
    }

    /* Info writes the message followed by a new line */
    [Fact]
    public void Info_Message_WritesLine()
    {
        Assert.Equal("loading" + NewLine, Capture(() => Terminal.Info("loading")));
    }

    /* Success writes the message followed by a new line */
    [Fact]
    public void Success_Message_WritesLine()
    {
        Assert.Equal("done" + NewLine, Capture(() => Terminal.Success("done")));
    }

    /* Warning writes the message followed by a new line */
    [Fact]
    public void Warning_Message_WritesLine()
    {
        Assert.Equal("careful" + NewLine, Capture(() => Terminal.Warning("careful")));
    }

    /* Error writes the message to standard output followed by a new line */
    [Fact]
    public void Error_Message_WritesLineToStandardOutput()
    {
        using var console = new CliConsoleCapture();

        Terminal.Error("broken");

        Assert.Equal("broken" + NewLine, console.Output);
        Assert.Empty(console.ErrorOutput);
    }

    /* Muted writes the message followed by a new line */
    [Fact]
    public void Muted_Message_WritesLine()
    {
        Assert.Equal("hint" + NewLine, Capture(() => Terminal.Muted("hint")));
    }

    /* Colored messages leave the console color reset */
    [Fact]
    public void Error_Message_ResetsForegroundColor()
    {
        using var console = new CliConsoleCapture();
        Console.ResetColor();
        var resetColor = Console.ForegroundColor;

        Terminal.Error("broken");

        Assert.Equal(resetColor, Console.ForegroundColor);
    }

    /* A table without headers prints nothing */
    [Fact]
    public void Table_NoHeaders_WritesNothing()
    {
        string output = Capture(() => Terminal.Table("Title", [], [["a"]]));

        Assert.Empty(output);
    }

    /* The title is printed as a banner on its own line */
    [Fact]
    public void Table_WithTitle_WritesTitleBanner()
    {
        string output = Capture(() => Terminal.Table("Models", ["Name"], [["a"]]));

        Assert.StartsWith(NewLine + "=== Models ===" + NewLine, output);
    }

    /* An empty title prints no banner */
    [Fact]
    public void Table_EmptyTitle_OmitsBanner()
    {
        string output = Capture(() => Terminal.Table("", ["Name"], [["a"]]));

        Assert.DoesNotContain("===", output);
        Assert.StartsWith("Name", output);
    }

    /* Columns are as wide as their widest cell and separated by two spaces, with a dash divider under the header */
    [Fact]
    public void Table_Rows_PadsColumnsToWidestCell()
    {
        string output = Capture(() => Terminal.Table("", ["Name", "Id"], [["alpha", "1"], ["b", "22"]]));

        Assert.Equal(["Name   Id", "-----  --", "alpha  1 ", "b      22", "", ""], CliOutput.Lines(output));
    }

    /* Missing cells of a short row are printed as blanks */
    [Fact]
    public void Table_RowShorterThanHeaders_PadsMissingCells()
    {
        string output = Capture(() => Terminal.Table("", ["Name", "Id"], [["alpha"]]));

        Assert.Equal("alpha" + "  " + "  ", CliOutput.Lines(output)[2]);
    }

    /* A null row is printed as an empty row */
    [Fact]
    public void Table_NullRow_PrintsBlankRow()
    {
        string output = Capture(() => Terminal.Table("", ["Name"], [null!, ["abc"]]));

        Assert.Equal(["Name", "----", "    ", "abc ", "", ""], CliOutput.Lines(output));
    }

    /* A cell too wide for the terminal is shortened with an ellipsis */
    [Fact]
    public void Table_CellWiderThanTerminal_TruncatesWithEllipsis()
    {
        string longValue = new('x', 300);

        string output = Capture(() => Terminal.Table("", ["Key", "Value"], [["k", longValue]]));

        string row = CliOutput.LineContaining(output, "xxx");
        Assert.DoesNotContain(longValue, row);
        Assert.EndsWith("...", row.TrimEnd());
    }

    /* When the columns cannot fit even at their minimum width, rows are printed as cards */
    [Fact]
    public void Table_ColumnsCannotFit_RendersCardView()
    {
        string first = new('A', 200), second = new('B', 200), third = new('C', 200);

        string output = Capture(() => Terminal.Table("", [first, second, third], [["one", "two", "three"]]));

        Assert.Equal(["• one", $"  {second}: two", $"  {third}: three", "", ""], CliOutput.Lines(output));
    }

    /* The card view keeps the title banner */
    [Fact]
    public void Table_CardViewWithTitle_WritesTitleBanner()
    {
        string wide = new('H', 200);

        string output = Capture(() => Terminal.Table("Voices", [wide, wide + "2", wide + "3"], [["one", "two", "three"]]));

        Assert.StartsWith(NewLine + "=== Voices ===" + NewLine + "• one", output);
    }

    /* In the card view, fields missing from a short row are skipped */
    [Fact]
    public void Table_CardViewRowShorterThanHeaders_SkipsMissingFields()
    {
        string wide = new('H', 200);

        string output = Capture(() => Terminal.Table("", [wide, wide + "2", wide + "3"], [["one", "two"]]));

        Assert.Equal(["• one", $"  {wide}2: two", "", ""], CliOutput.Lines(output));
    }

    /* The progress bar fills proportionally and stays on the same line below 100% */
    [Fact]
    public void RenderProgressBar_HalfWay_DrawsHalfFilledBarWithoutNewLine()
    {
        string output = Capture(() => Terminal.RenderProgressBar("Downloading", 50));

        Assert.Equal($"\rDownloading [{new string('#', 15)}{new string('-', 15)}]  50%", output);
    }

    /* At 100% the bar is full and the line is ended */
    [Fact]
    public void RenderProgressBar_Complete_DrawsFullBarAndEndsLine()
    {
        string output = Capture(() => Terminal.RenderProgressBar("Downloading", 100));

        Assert.Equal($"\rDownloading [{new string('#', 30)}] 100%" + NewLine, output);
    }

    /* Values above 100 are drawn as 100% */
    [Fact]
    public void RenderProgressBar_AboveHundred_ClampsToHundred()
    {
        string output = Capture(() => Terminal.RenderProgressBar("x", 250));

        Assert.Equal($"\rx [{new string('#', 30)}] 100%" + NewLine, output);
    }

    /* Negative values are drawn as 0% */
    [Fact]
    public void RenderProgressBar_Negative_ClampsToZero()
    {
        string output = Capture(() => Terminal.RenderProgressBar("x", -20));

        Assert.Equal($"\rx [{new string('-', 30)}]   0%", output);
    }

    /* A custom bar width is honoured */
    [Fact]
    public void RenderProgressBar_CustomWidth_UsesWidth()
    {
        string output = Capture(() => Terminal.RenderProgressBar("x", 30, barWidth: 10));

        Assert.Equal("\rx [###-------]  30%", output);
    }

    /* The prompt shows the question with the [y/N] hint when the default is No */
    [Fact]
    public void Confirm_DefaultNo_WritesPromptWithLowercaseYesHint()
    {
        var (_, output) = ConfirmWith("y\n");

        Assert.Equal("Continue? [y/N]: ", output);
    }

    /* The prompt shows the [Y/n] hint when the default is Yes */
    [Fact]
    public void Confirm_DefaultYes_WritesPromptWithUppercaseYesHint()
    {
        var (_, output) = ConfirmWith("n\n", defaultYes: true);

        Assert.Equal("Continue? [Y/n]: ", output);
    }

    /* "y" and "yes" are accepted, ignoring case and surrounding spaces */
    [Theory]
    [InlineData("y\n")]
    [InlineData("yes\n")]
    [InlineData("  YES  \n")]
    [InlineData("Y\n")]
    public void Confirm_YesAnswer_ReturnsTrue(string stdin)
    {
        Assert.True(ConfirmWith(stdin).Answer);
    }

    /* Any other answer is a No, even when the default is Yes */
    [Theory]
    [InlineData("n\n")]
    [InlineData("no\n")]
    [InlineData("maybe\n")]
    public void Confirm_OtherAnswer_ReturnsFalse(string stdin)
    {
        Assert.False(ConfirmWith(stdin, defaultYes: true).Answer);
    }

    /* Pressing Enter (or typing only spaces) takes the default */
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("   \n", true)]
    public void Confirm_EmptyAnswer_ReturnsDefault(string stdin, bool defaultYes)
    {
        Assert.Equal(defaultYes, ConfirmWith(stdin, defaultYes).Answer);
    }

    /* Closed stdin (EOF, e.g. piped input) takes the default */
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Confirm_EndOfInput_ReturnsDefault(bool defaultYes)
    {
        Assert.Equal(defaultYes, ConfirmWith("", defaultYes).Answer);
    }
}
