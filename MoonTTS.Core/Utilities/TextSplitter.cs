using System.Text.RegularExpressions;

namespace MoonTTS.Core.Utilities;

public class TextSplitter
{
    /* Splits the text on punctuation */
    private static readonly Regex SentenceRegex = new(
        @"[^.!?;\n\r]+(?:[.!?;\n\r]+|\s*$)",
        RegexOptions.Compiled);

    public static List<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();
        
        var matches = SentenceRegex.Matches(text);
        var sentences = new List<string>();

        foreach (Match match in matches)
        {
            string trimmed = match.Value.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                sentences.Add(trimmed);
        }
        return sentences.Count > 0 ? sentences : new List<string> { text.Trim() };
    }
}