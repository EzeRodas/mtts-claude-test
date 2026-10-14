using System.Text;
using System.Text.RegularExpressions;

namespace MoonTTS.Core.Utilities;

public class PathSanitizer
{
    public const string DefaultFallbackName = "new_profile";
    
    private static readonly HashSet<char> InvalidChars =
        Path.GetInvalidFileNameChars()
            .Concat(Path.GetInvalidPathChars())
            .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            .ToHashSet();

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /* Makes sure that the profile name won't contain any character or phrase restricted by different
       OS's for folder creation, as each audio files will go into their respective profile folder */
    public static string SanitizeFolderName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return DefaultFallbackName;

        var sb = new StringBuilder();
        foreach (char c in rawName.Trim())
        {
            /* Replaces invalid chars with "_" */
            sb.Append(InvalidChars.Contains(c) ? '_' : c);
        }

        /* Collapses multiple consecutive "_" into a single one */
        string sanitized = Regex.Replace(sb.ToString(), @"_+", "_"); 

        /* Trims these characters at the end */
        sanitized = sb.ToString().Trim(' ', '.', '_');

        /* If nothing valid remains falls back to default */
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = DefaultFallbackName;

        /* Prevents windows reserved names by appending "_profile" */
        if (ReservedWindowsNames.Contains(sanitized))
            sanitized = $"{sanitized}_profile";

        return sanitized;
    }
}