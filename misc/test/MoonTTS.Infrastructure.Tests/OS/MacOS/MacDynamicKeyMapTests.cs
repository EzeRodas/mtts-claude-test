using MoonTTS.Infrastructure.OS.MacOS;

namespace MoonTTS.Infrastructure.Tests.OS.MacOS;

/* Builds MacDynamicKeyMap from DummyMacKeyboardLayoutApi, which answers like the macOS text-input APIs */
public sealed class MacDynamicKeyMapTests
{
    /* Ways the layout can be unavailable, each of which must end in the QWERTY fallback */
    public enum LayoutFailure
    {
        NoInputSource,
        NoLayoutData,
        MissingSymbol
    }

    /* A letter resolves to the keycode that produces it on the current (US) layout */
    [Fact]
    public void TryGetKeyCode_LetterOnUsLayout_ReturnsLayoutKeycode()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        bool found = map.TryGetKeyCode("M", out uint keyCode);

        Assert.True(found);
        Assert.Equal(0x2Eu, keyCode);
    }

    /* On AZERTY "A" lives on the physical key US calls Q, and the map follows the layout */
    [Theory]
    [InlineData("A", 0x0Cu)]
    [InlineData("Q", 0x00u)]
    [InlineData("Z", 0x0Du)]
    [InlineData("M", 0x29u)]
    public void TryGetKeyCode_LetterOnAzertyLayout_ReturnsKeycodeThatProducesIt(string key, uint expected)
    {
        using var layout = DummyMacKeyboardLayoutApi.AzertyLayout();
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode(key, out uint keyCode);

        Assert.Equal(expected, keyCode);
    }

    /* Key names match regardless of case */
    [Fact]
    public void TryGetKeyCode_LowercaseName_MatchesUppercaseEntry()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        bool found = map.TryGetKeyCode("m", out uint keyCode);

        Assert.True(found);
        Assert.Equal(0x2Eu, keyCode);
    }

    /* Punctuation produced by the layout is mapped too */
    [Fact]
    public void TryGetKeyCode_LayoutPunctuation_ReturnsLayoutKeycode()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode(",", out uint keyCode);

        Assert.Equal(0x2Bu, keyCode);
    }

    /* A character no key of the layout produces is not found */
    [Fact]
    public void TryGetKeyCode_CharacterNotOnLayout_ReturnsFalse()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        bool found = map.TryGetKeyCode("Ñ", out _);

        Assert.False(found);
    }

    /* Function, navigation and editing keys use their fixed Apple keycodes */
    [Theory]
    [InlineData("F1", 0x7Au)]
    [InlineData("F12", 0x6Fu)]
    [InlineData("SPACE", 0x31u)]
    [InlineData("ENTER", 0x24u)]
    [InlineData("ESC", 0x35u)]
    [InlineData("BACKSPACE", 0x33u)]
    [InlineData("PAGEDOWN", 0x79u)]
    [InlineData("UPARROW", 0x7Eu)]
    public void TryGetKeyCode_NonPrintableKey_ReturnsFixedKeycode(string key, uint expected)
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        bool found = map.TryGetKeyCode(key, out uint keyCode);

        Assert.True(found);
        Assert.Equal(expected, keyCode);
    }

    /* When two physical keys produce the same character the lowest keycode wins */
    [Fact]
    public void TryGetKeyCode_TwoKeysProduceSameCharacter_ReturnsLowestKeycode()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        layout.Layout[0x32] = "<";
        layout.Layout[0x0A] = "<";
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("<", out uint keyCode);

        Assert.Equal(0x0Au, keyCode);
    }

    /* Every keycode 0-127 is translated exactly once */
    [Fact]
    public void TryGetKeyCode_FirstLookup_TranslatesAll128Keycodes()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);

        Assert.Equal(Enumerable.Range(0, 128).Select(i => (uint)i), layout.Translations.Select(t => t.VirtualKeyCode));
    }

    /* Keys are translated on key-down, without modifiers, for the physical keyboard type, into a 4-char buffer */
    [Fact]
    public void TryGetKeyCode_FirstLookup_TranslatesKeyDownWithoutModifiers()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        layout.KeyboardType = 41;
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);

        Assert.All(layout.Translations, t =>
        {
            Assert.Equal((ushort)0, t.KeyAction);
            Assert.Equal(0u, t.ModifierKeyState);
            Assert.Equal(41u, t.KeyboardType);
            Assert.Equal(4u, t.MaxLength);
        });
    }

    /* The layout is read once; later lookups use the cached map */
    [Fact]
    public void TryGetKeyCode_CalledTwice_ReadsLayoutOnce()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);
        map.TryGetKeyCode("B", out _);

        Assert.Equal(1, layout.CopyInputSourceCount);
        Assert.Equal(128, layout.Translations.Count);
    }

    /* The layout is looked up through the Carbon framework */
    [Fact]
    public void TryGetKeyCode_FirstLookup_OpensCarbonFramework()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);

        Assert.Equal([DummyMacKeyboardLayoutApi.CarbonPath], layout.OpenedPaths);
    }

    /* The retained input source is released after the layout was read */
    [Fact]
    public void TryGetKeyCode_LayoutRead_ReleasesInputSource()
    {
        using var layout = DummyMacKeyboardLayoutApi.UsLayout();
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);

        Assert.Equal(0, layout.LeakedInputSourceRetains);
    }

    /* Without a readable layout, letters and digits use the US QWERTY keycodes */
    [Theory]
    [InlineData(LayoutFailure.NoInputSource)]
    [InlineData(LayoutFailure.NoLayoutData)]
    [InlineData(LayoutFailure.MissingSymbol)]
    public void TryGetKeyCode_LayoutUnavailable_UsesQwertyFallback(LayoutFailure failure)
    {
        using var layout = BrokenAzertyLayout(failure);
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out uint a);
        map.TryGetKeyCode("1", out uint one);

        Assert.Equal(0x00u, a);
        Assert.Equal(0x12u, one);
    }

    /* The QWERTY fallback still contains the fixed non-printable keys */
    [Theory]
    [InlineData(LayoutFailure.NoInputSource)]
    [InlineData(LayoutFailure.NoLayoutData)]
    [InlineData(LayoutFailure.MissingSymbol)]
    public void TryGetKeyCode_LayoutUnavailable_KeepsNonPrintableKeys(LayoutFailure failure)
    {
        using var layout = BrokenAzertyLayout(failure);
        var map = new MacDynamicKeyMap(layout);

        bool found = map.TryGetKeyCode("F5", out uint keyCode);

        Assert.True(found);
        Assert.Equal(0x60u, keyCode);
    }

    /* Without a readable layout no key is translated */
    [Theory]
    [InlineData(LayoutFailure.NoInputSource)]
    [InlineData(LayoutFailure.NoLayoutData)]
    [InlineData(LayoutFailure.MissingSymbol)]
    public void TryGetKeyCode_LayoutUnavailable_DoesNotTranslateKeys(LayoutFailure failure)
    {
        using var layout = BrokenAzertyLayout(failure);
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);

        Assert.Empty(layout.Translations);
    }

    /* When the input source has no layout data, the retained input source is still released */
    [Fact]
    public void TryGetKeyCode_NoLayoutData_ReleasesInputSource()
    {
        using var layout = BrokenAzertyLayout(LayoutFailure.NoLayoutData);
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);

        Assert.Equal(0, layout.LeakedInputSourceRetains);
    }

    /* The Carbon handle is closed on every path, fallbacks included */
    [Theory]
    [InlineData(null)]
    [InlineData(LayoutFailure.NoInputSource)]
    [InlineData(LayoutFailure.NoLayoutData)]
    [InlineData(LayoutFailure.MissingSymbol)]
    public void TryGetKeyCode_AnyPath_ClosesCarbonHandle(LayoutFailure? failure)
    {
        using var layout = failure is { } f ? BrokenAzertyLayout(f) : DummyMacKeyboardLayoutApi.AzertyLayout();
        var map = new MacDynamicKeyMap(layout);

        map.TryGetKeyCode("A", out _);

        Assert.Equal([DummyMacKeyboardLayoutApi.CarbonHandle], layout.ClosedHandles);
    }

    /* An AZERTY layout whose data can't be read in the given way */
    private static DummyMacKeyboardLayoutApi BrokenAzertyLayout(LayoutFailure failure)
    {
        var layout = DummyMacKeyboardLayoutApi.AzertyLayout();
        switch (failure)
        {
            case LayoutFailure.NoInputSource: layout.HasCurrentInputSource = false; break;
            case LayoutFailure.NoLayoutData: layout.HasLayoutData = false; break;
            case LayoutFailure.MissingSymbol: layout.ExportsLayoutSymbol = false; break;
        }
        return layout;
    }
}
