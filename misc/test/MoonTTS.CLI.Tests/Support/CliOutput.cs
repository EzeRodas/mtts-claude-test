namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - Lines(string output) : string[]
        - LineContaining(string output, string text) : string
*/

/* Helpers to read what the CLI printed */
public static class CliOutput
{
    /* Splits printed output into lines, without line terminators */
    public static string[] Lines(string output) =>
        output.Replace("\r\n", "\n").Split('\n');

    /* Returns the single printed line containing `text` (fails the test when there is none or several) */
    public static string LineContaining(string output, string text) =>
        Assert.Single(Lines(output), line => line.Contains(text, StringComparison.Ordinal));
}
