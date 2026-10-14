namespace MoonTTS.CLI.Utilities;

/* Helper for styling the terminal */

public class Terminal
{
    /* Colored text outputs */
    public static void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        WriteAndReset(message);
    }
    
    public static void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        WriteAndReset(message);
    }
    
    public static void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        WriteAndReset(message);
    }
    
    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        WriteAndReset(message);
    }
    
    public static void Muted(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        WriteAndReset(message);
    }

    /* To not duplicate code */
    private static void WriteAndReset(string message)
    {
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /* Renders a table */
    public static void Table(string title, string[] headers, IEnumerable<string[]> rows)
    {
        var rowList = rows.Select(r => r ?? Array.Empty<string>()).ToList();
        int colCount = headers.Length;
        if (colCount == 0) return;

        /* Safely query terminal width; fallback to 120 if redirected or headless */
        int termWidth = 120;
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowWidth > 20)
                termWidth = Console.WindowWidth;
        }
        catch
        {
            termWidth = 120;
        }

        /* Calculate natural max width per column */
        int[] widths = new int[colCount];
        for (int i = 0; i < colCount; i++)
        {
            int max = headers[i].Length;
            foreach (var row in rowList)
            {
                if (i < row.Length && row[i] != null)
                    max = Math.Max(max, row[i].Length);
            }
            widths[i] = max;
        }

        const int colGap = 2;
        int totalGap = (colCount - 1) * colGap;
        int totalNeeded = widths.Sum() + totalGap;

        /* Iteratively shrink widest columns when overflowing terminal width */
        while (totalNeeded > termWidth)
        {
            int widestCol = -1;
            int maxW = 0;
            for (int i = 0; i < colCount; i++)
            {
                int minW = Math.Max(headers[i].Length, 6);
                if (widths[i] > minW && widths[i] > maxW)
                {
                    maxW = widths[i];
                    widestCol = i;
                }
            }

            if (widestCol == -1)
                break;

            widths[widestCol]--;
            totalNeeded--;
        }

        /* If terminal is ultra-narrow or columns cannot fit without breaking, switch to card view */
        if (termWidth < 50 || totalNeeded > termWidth)
        {
            RenderCardView(title, headers, rowList);
            return;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n=== {title} ===");
            Console.ResetColor();
        }

        /* Header */
        Console.ForegroundColor = ConsoleColor.White;
        for (int i = 0; i < colCount; i++)
        {
            string h = Truncate(headers[i], widths[i]);
            Console.Write(h.PadRight(widths[i]));
            if (i < colCount - 1) Console.Write(new string(' ', colGap));
        }
        Console.WriteLine();

        /* Divider */
        Console.ForegroundColor = ConsoleColor.DarkGray;
        for (int i = 0; i < colCount; i++)
        {
            Console.Write(new string('-', widths[i]));
            if (i < colCount - 1) Console.Write(new string(' ', colGap));
        }
        Console.WriteLine();
        Console.ResetColor();

        /* Rows */
        foreach (var row in rowList)
        {
            for (int i = 0; i < colCount; i++)
            {
                string raw = (i < row.Length && row[i] != null) ? row[i] : "";
                string cell = Truncate(raw, widths[i]);

                if (cell == "ACTIVE" || cell == "Yes" || cell.Contains("Enabled"))
                    Console.ForegroundColor = ConsoleColor.Green;
                else if (cell == "No" || cell.Contains("Disabled"))
                    Console.ForegroundColor = ConsoleColor.Red;
                else
                    Console.ForegroundColor = ConsoleColor.White;

                Console.Write(cell.PadRight(widths[i]));
                Console.ResetColor();

                if (i < colCount - 1) Console.Write(new string(' ', colGap));
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    /* Renders a vertical key-value list for narrow displays */
    private static void RenderCardView(string title, string[] headers, List<string[]> rows)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n=== {title} ===");
            Console.ResetColor();
        }

        foreach (var row in rows)
        {
            Console.ForegroundColor = ConsoleColor.White;
            string primary = row.Length > 0 ? row[0] : "";
            Console.WriteLine($"• {primary}");
            Console.ResetColor();

            for (int i = 1; i < headers.Length && i < row.Length; i++)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {headers[i]}: ");
                Console.ResetColor();

                string val = row[i];
                if (val == "ACTIVE" || val == "Yes")
                    Console.ForegroundColor = ConsoleColor.Green;
                else if (val == "No")
                    Console.ForegroundColor = ConsoleColor.Red;
                else
                    Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine(val);
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    /* Truncates text exceeding maximum column width with an ellipsis */
    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        if (max <= 3) return text[..max];
        return text[..(max - 3)] + "...";
    }

    public static void RenderProgressBar(string prefix, int percentage, int barWidth = 30)
    {
        int clamped = Math.Clamp(percentage, 0, 100);
        int filled = (int) Math.Round((double)clamped / 100 * barWidth);
        string bar = new string('#', filled) + new string('-', barWidth - filled);
        Console.Write($"\r{prefix} [{bar}] {clamped,3}%");
        if (clamped >= 100) Console.WriteLine();
    }

    public static bool Confirm(string prompt, bool defaultYes = false)
    {
        string hint = defaultYes ? "Y/n" : "y/N";
        Console.Write($"{prompt} [{hint}]: ");
        string? input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(input)) return defaultYes;
        return input == "y" || input == "yes";
    }
}