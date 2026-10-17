using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Tests.ValueObjects;

/* Verifies validation, parsing and formatting of ShortcutCombo */
public sealed class ShortcutComboTests
{
    /* The constructor stores each modifier flag as given */
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    public void Constructor_Modifiers_StoresFlags(bool ctrl, bool alt, bool shift, bool win)
    {
        var combo = new ShortcutCombo(ctrl, alt, shift, win, "M");

        Assert.Equal(ctrl, combo.HasCtrl);
        Assert.Equal(alt, combo.HasAlt);
        Assert.Equal(shift, combo.HasShift);
        Assert.Equal(win, combo.HasWin);
    }

    /* The main key is trimmed and upper-cased */
    [Theory]
    [InlineData("m", "M")]
    [InlineData("  f5 ", "F5")]
    [InlineData("Space", "SPACE")]
    public void Constructor_MainKey_IsTrimmedAndUpperCased(string mainKey, string expected)
    {
        var combo = new ShortcutCombo(true, false, false, false, mainKey);

        Assert.Equal(expected, combo.MainKey);
    }

    /* A missing main key is rejected, naming the mainKey parameter */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_MissingMainKey_ThrowsArgumentNullException(string? mainKey)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ShortcutCombo(true, false, false, false, mainKey!));

        Assert.Equal("mainKey", exception.ParamName);
    }

    /* A combo without any modifier is rejected so plain typing is never captured */
    [Fact]
    public void Constructor_NoModifier_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ShortcutCombo(false, false, false, false, "M"));

        Assert.Equal("mainKey", exception.ParamName);
    }

    /* Blank input does not parse and outputs null */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_BlankInput_ReturnsFalseAndNull(string? input)
    {
        bool parsed = ShortcutCombo.TryParse(input, out var combo);

        Assert.False(parsed);
        Assert.Null(combo);
    }

    /* Each modifier name (and its alias) is recognised regardless of case */
    [Theory]
    [InlineData("Ctrl+M", true, false, false, false)]
    [InlineData("ctrl+M", true, false, false, false)]
    [InlineData("Control+M", true, false, false, false)]
    [InlineData("CONTROL+M", true, false, false, false)]
    [InlineData("Alt+M", false, true, false, false)]
    [InlineData("aLt+M", false, true, false, false)]
    [InlineData("Shift+M", false, false, true, false)]
    [InlineData("SHIFT+M", false, false, true, false)]
    [InlineData("Win+M", false, false, false, true)]
    [InlineData("Super+M", false, false, false, true)]
    [InlineData("super+M", false, false, false, true)]
    [InlineData("Ctrl+Alt+Shift+Win+M", true, true, true, true)]
    public void TryParse_Modifier_SetsFlag(string input, bool ctrl, bool alt, bool shift, bool win)
    {
        bool parsed = ShortcutCombo.TryParse(input, out var combo);

        Assert.True(parsed);
        Assert.NotNull(combo);
        Assert.Equal(ctrl, combo.HasCtrl);
        Assert.Equal(alt, combo.HasAlt);
        Assert.Equal(shift, combo.HasShift);
        Assert.Equal(win, combo.HasWin);
    }

    /* The non-modifier token becomes the upper-cased main key */
    [Fact]
    public void TryParse_ValidCombo_UpperCasesMainKey()
    {
        ShortcutCombo.TryParse("Ctrl+Alt+m", out var combo);

        Assert.Equal("M", combo!.MainKey);
    }

    /* Modifiers may come after the main key */
    [Fact]
    public void TryParse_MainKeyFirst_IsParsed()
    {
        bool parsed = ShortcutCombo.TryParse("M+Ctrl", out var combo);

        Assert.True(parsed);
        Assert.True(combo!.HasCtrl);
        Assert.Equal("M", combo.MainKey);
    }

    /* Spaces around tokens and empty tokens are ignored */
    [Theory]
    [InlineData(" Ctrl + Alt + M ")]
    [InlineData("Ctrl++Alt+M")]
    [InlineData("+Ctrl+Alt+M+")]
    public void TryParse_SpacesAndEmptyTokens_AreIgnored(string input)
    {
        bool parsed = ShortcutCombo.TryParse(input, out var combo);

        Assert.True(parsed);
        Assert.Equal("Ctrl+Alt+M", combo!.ToString());
    }

    /* A combo with only modifiers and no main key does not parse */
    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Alt+Shift+Win")]
    [InlineData("Ctrl+ +Shift")]
    public void TryParse_NoMainKey_ReturnsFalseAndNull(string input)
    {
        bool parsed = ShortcutCombo.TryParse(input, out var combo);

        Assert.False(parsed);
        Assert.Null(combo);
    }

    /* A main key without any modifier does not parse */
    [Theory]
    [InlineData("M")]
    [InlineData("F5")]
    [InlineData("+M+")]
    public void TryParse_NoModifier_ReturnsFalseAndNull(string input)
    {
        bool parsed = ShortcutCombo.TryParse(input, out var combo);

        Assert.False(parsed);
        Assert.Null(combo);
    }

    /* ToString lists the modifiers in canonical order (Ctrl, Alt, Shift, Win) followed by the main key */
    [Theory]
    [InlineData(true, false, false, false, "Ctrl+M")]
    [InlineData(false, true, false, false, "Alt+M")]
    [InlineData(false, false, true, false, "Shift+M")]
    [InlineData(false, false, false, true, "Win+M")]
    [InlineData(true, true, false, false, "Ctrl+Alt+M")]
    [InlineData(false, true, true, true, "Alt+Shift+Win+M")]
    [InlineData(true, true, true, true, "Ctrl+Alt+Shift+Win+M")]
    public void ToString_Modifiers_UsesCanonicalOrder(bool ctrl, bool alt, bool shift, bool win, string expected)
    {
        var combo = new ShortcutCombo(ctrl, alt, shift, win, "m");

        Assert.Equal(expected, combo.ToString());
    }

    /* Parsing normalises aliases, case and order into the canonical text */
    [Theory]
    [InlineData("super+shift+a", "Shift+Win+A")]
    [InlineData("control+alt+m", "Ctrl+Alt+M")]
    [InlineData("M+Alt+Ctrl", "Ctrl+Alt+M")]
    public void ToString_AfterTryParse_IsCanonical(string input, string expected)
    {
        ShortcutCombo.TryParse(input, out var combo);

        Assert.Equal(expected, combo!.ToString());
    }

    /* Formatting a combo and parsing the text again keeps every component */
    [Fact]
    public void ToString_ThenTryParse_RoundTrips()
    {
        var original = new ShortcutCombo(true, false, true, true, "F12");

        ShortcutCombo.TryParse(original.ToString(), out var parsed);

        Assert.NotNull(parsed);
        Assert.Equal(original.HasCtrl, parsed.HasCtrl);
        Assert.Equal(original.HasAlt, parsed.HasAlt);
        Assert.Equal(original.HasShift, parsed.HasShift);
        Assert.Equal(original.HasWin, parsed.HasWin);
        Assert.Equal(original.MainKey, parsed.MainKey);
    }
}
