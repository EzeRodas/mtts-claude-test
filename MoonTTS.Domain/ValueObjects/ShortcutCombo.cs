namespace MoonTTS.Domain.ValueObjects;

public class ShortcutCombo
{
    public bool HasCtrl { get; }
    public bool HasAlt { get; }
    public bool HasShift { get; }
    public bool HasWin { get; }
    public string MainKey { get; }

    public ShortcutCombo(bool ctrl, bool alt, bool shift, bool win, string mainKey)
    {
        /* Throws an error if it's created without a main key (Ex. "Ctrl+Shift") */
        if (string.IsNullOrEmpty(mainKey))
        {
            throw new ArgumentNullException("The shortcut must contain a main key.");
        }

        /* Validates that at least one modifier key is provided to prevent capturing plain typing keys */
        if (!ctrl && !alt && !shift && !win)
        {
            throw new ArgumentException(
                "The shortcut must contain at least one modifier key (Ctrl, Alt, Shift, or Win).",
                nameof(mainKey)
            );
        }

        HasCtrl = ctrl;
        HasAlt = alt;
        HasShift = shift;
        HasWin = win;
        /* Since we're defining this as Uppercase here, it'll have to be converted to lowercase for systems like
           X11 manually */
        MainKey = mainKey.Trim().ToUpperInvariant();
    }

    /* Parses a string representation into a ShortcutCombo instance */
    public static bool TryParse(string? input, out ShortcutCombo? combo)
    {
        combo = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var tokens = input.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        bool ctrl = false,
            alt = false,
            shift = false,
            win = false;
        string? mainKey = null;

        foreach (var token in tokens)
        {
            if (
                token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Control", StringComparison.OrdinalIgnoreCase)
            )
                ctrl = true;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                alt = true;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                shift = true;
            else if (
                token.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Super", StringComparison.OrdinalIgnoreCase)
            )
                win = true;
            else
                mainKey = token;
        }

        if (string.IsNullOrEmpty(mainKey) || (!ctrl && !alt && !shift && !win))
            return false;

        combo = new ShortcutCombo(ctrl, alt, shift, win, mainKey);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (HasCtrl)
            parts.Add("Ctrl");
        if (HasAlt)
            parts.Add("Alt");
        if (HasShift)
            parts.Add("Shift");
        if (HasWin)
            parts.Add("Win");
        parts.Add(MainKey);

        return string.Join("+", parts);
    }
}
