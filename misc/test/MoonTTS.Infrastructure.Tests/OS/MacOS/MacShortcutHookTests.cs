using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.OS.MacOS;

namespace MoonTTS.Infrastructure.Tests.OS.MacOS;

/* Drives MacShortcutHook against DummyMacCarbonHotKeyApi (Carbon Event Manager) and DummyMacKeyboardLayoutApi */
public sealed class MacShortcutHookTests : IDisposable
{
    /* Carbon modifier masks (Events.h) */
    private const uint CmdKey = 0x0100;
    private const uint ShiftKey = 0x0200;
    private const uint OptionKey = 0x0800;
    private const uint ControlKey = 0x1000;

    /* US keycodes of the keys used below */
    private const uint KeyM = 0x2E;
    private const uint KeyP = 0x23;
    private const uint KeyT = 0x11;

    private static readonly uint MoonTtsSignature = DummyMacCarbonHotKeyApi.FourCC("MTTS");

    private readonly DummyMacCarbonHotKeyApi _carbon = new();
    private readonly DummyMacKeyboardLayoutApi _layout = DummyMacKeyboardLayoutApi.UsLayout();
    private readonly MacShortcutHook _hook;
    private readonly List<string> _triggered = new();

    /* Every test gets a hook wired to fresh dummies, recording what it reports */
    public MacShortcutHookTests()
    {
        _hook = new MacShortcutHook(_carbon, _layout);
        _hook.OnShortcutTriggered += _triggered.Add;
    }

    /* Releases the hook and the dummy layout memory */
    public void Dispose()
    {
        _hook.Dispose();
        _layout.Dispose();
    }

    /* InitializeAsync installs exactly one application handler for (kEventClassKeyboard, kEventHotKeyPressed) */
    [Fact]
    public async Task InitializeAsync_Called_InstallsHotKeyPressedHandler()
    {
        await _hook.InitializeAsync();

        var handler = Assert.Single(_carbon.Handlers);
        var type = Assert.Single(handler.Types);
        Assert.Equal(DummyMacCarbonHotKeyApi.FourCC("keyb"), type.eventClass);
        Assert.Equal(5u, type.eventKind);
    }

    /* The toggle shortcut is registered with the layout keycode, Carbon modifier masks and the MTTS signature */
    [Fact]
    public async Task RegisterShortcutAsync_ToggleShortcut_RegistersCtrlOptionM()
    {
        await _hook.InitializeAsync();

        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        var call = Assert.Single(_carbon.RegisterCalls);
        Assert.Equal(KeyM, call.KeyCode);
        Assert.Equal(ControlKey | OptionKey, call.Modifiers);
        Assert.Equal(MoonTtsSignature, call.HotKeyId.signature);
        Assert.Equal(1u, call.HotKeyId.id);
    }

    /* Hotkeys are registered on the application event target with no options */
    [Fact]
    public async Task RegisterShortcutAsync_Called_TargetsApplicationWithoutOptions()
    {
        await _hook.InitializeAsync();

        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());

        var call = Assert.Single(_carbon.RegisterCalls);
        Assert.Equal(DummyMacCarbonHotKeyApi.ApplicationTarget, call.Target);
        Assert.Equal(0u, call.Options);
    }

    /* Each modifier maps to its Carbon mask; Win is the Command key */
    [Theory]
    [InlineData(true, false, false, false, ControlKey)]
    [InlineData(false, true, false, false, OptionKey)]
    [InlineData(false, false, true, false, ShiftKey)]
    [InlineData(false, false, false, true, CmdKey)]
    [InlineData(true, true, true, true, ControlKey | OptionKey | ShiftKey | CmdKey)]
    public async Task RegisterShortcutAsync_Modifiers_MapToCarbonMasks(bool ctrl, bool alt, bool shift, bool win, uint expected)
    {
        await _hook.InitializeAsync();

        await _hook.RegisterShortcutAsync(new ShortcutCombo(ctrl, alt, shift, win, "M"), new());

        Assert.Equal(expected, Assert.Single(_carbon.RegisterCalls).Modifiers);
    }

    /* On a non-US layout the keycode of the key that produces the letter is registered */
    [Fact]
    public async Task RegisterShortcutAsync_AzertyLayout_RegistersLayoutKeycode()
    {
        using var azerty = DummyMacKeyboardLayoutApi.AzertyLayout();
        using var hook = new MacShortcutHook(_carbon, azerty);
        await hook.InitializeAsync();

        await hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "A"), new());

        Assert.Equal(0x0Cu, Assert.Single(_carbon.RegisterCalls).KeyCode);
    }

    /* A key the layout can't produce is skipped; the other shortcuts are still registered */
    [Fact]
    public async Task RegisterShortcutAsync_KeyNotOnLayout_IsSkipped()
    {
        await _hook.InitializeAsync();

        await _hook.RegisterShortcutAsync(
            new ShortcutCombo(true, false, false, false, "Ñ"),
            new() { ["p"] = new ShortcutCombo(true, false, false, false, "P") });

        Assert.Equal([KeyP], _carbon.RegisterCalls.Select(c => c.KeyCode));
    }

    /* Without a toggle shortcut only the phrases are registered */
    [Fact]
    public async Task RegisterShortcutAsync_NullToggle_RegistersOnlyPhrases()
    {
        await _hook.InitializeAsync();

        await _hook.RegisterShortcutAsync(null, new() { ["p"] = new ShortcutCombo(true, false, false, false, "P") });

        Assert.Equal([KeyP], _carbon.HotKeys.Select(h => h.KeyCode));
    }

    /* Toggle and phrases get consecutive ids starting at 1 */
    [Fact]
    public async Task RegisterShortcutAsync_ToggleAndPhrases_UseConsecutiveIds()
    {
        await _hook.InitializeAsync();

        await _hook.RegisterShortcutAsync(
            new ShortcutCombo(true, false, false, false, "M"),
            new() { ["p"] = new ShortcutCombo(true, false, false, false, "P"), ["t"] = new ShortcutCombo(true, false, false, false, "T") });

        Assert.Equal([1u, 2u, 3u], _carbon.HotKeys.Select(h => h.HotKeyId.id).Order());
    }

    /* Registering again unregisters every hotkey ref obtained before */
    [Fact]
    public async Task RegisterShortcutAsync_CalledAgain_UnregistersPreviousHotKeys()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(
            new ShortcutCombo(true, false, false, false, "M"),
            new() { ["p"] = new ShortcutCombo(true, false, false, false, "P") });
        var oldRefs = _carbon.HotKeys.Select(h => h.Ref).ToList();

        await _hook.RegisterShortcutAsync(new ShortcutCombo(false, true, false, false, "T"), new());

        Assert.Equal(oldRefs.Order(), _carbon.UnregisteredRefs.Order());
    }

    /* After re-registration only the new set is active, numbered again from 1 */
    [Fact]
    public async Task RegisterShortcutAsync_CalledAgain_KeepsOnlyNewHotKeys()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());

        await _hook.RegisterShortcutAsync(new ShortcutCombo(false, true, false, false, "T"), new());

        var hotKey = Assert.Single(_carbon.HotKeys);
        Assert.Equal((KeyT, OptionKey, 1u), (hotKey.KeyCode, hotKey.Modifiers, hotKey.HotKeyId.id));
    }

    /* A combo the system already owns is refused; the remaining shortcuts still fire */
    [Fact]
    public async Task RegisterShortcutAsync_ComboOwnedBySystem_OtherShortcutsStillFire()
    {
        _carbon.ReserveForSystem(KeyM, CmdKey);
        await _hook.InitializeAsync();

        await _hook.RegisterShortcutAsync(
            new ShortcutCombo(false, false, false, true, "M"),
            new() { ["p"] = new ShortcutCombo(true, false, false, false, "P") });
        _carbon.Press(KeyP, ControlKey);

        Assert.Equal(["phrase_p"], _triggered);
    }

    /* A refused combo is not tracked, so nothing is unregistered for it later */
    [Fact]
    public async Task RegisterShortcutAsync_ComboOwnedBySystem_IsNotUnregisteredLater()
    {
        _carbon.ReserveForSystem(KeyM, CmdKey);
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(false, false, false, true, "M"), new());

        await _hook.RegisterShortcutAsync(null, new());

        Assert.Empty(_carbon.UnregisteredRefs);
    }

    /* Pressing the toggle combo raises "toggle_app" */
    [Fact]
    public async Task HandleHotKey_TogglePressed_RaisesToggleApp()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        _carbon.Press(KeyM, ControlKey | OptionKey);

        Assert.Equal(["toggle_app"], _triggered);
    }

    /* Pressing a phrase combo raises "phrase_<id>" */
    [Fact]
    public async Task HandleHotKey_PhrasePressed_RaisesPhraseId()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(null, new() { ["thanks"] = new ShortcutCombo(false, false, true, false, "T") });

        _carbon.Press(KeyT, ShiftKey);

        Assert.Equal(["phrase_thanks"], _triggered);
    }

    /* The handler reports the event as handled (noErr) */
    [Fact]
    public async Task HandleHotKey_OwnHotKey_ReturnsNoErr()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());

        int? status = _carbon.Press(KeyM, ControlKey);

        Assert.Equal(DummyMacCarbonHotKeyApi.NoErr, status);
    }

    /* An event carrying another signature is not ours and raises nothing */
    [Fact]
    public async Task HandleHotKey_ForeignSignature_IsIgnored()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());

        _carbon.DeliverHotKeyEvent(new IMacCarbonHotKeyApi.EventHotKeyID { signature = DummyMacCarbonHotKeyApi.FourCC("ABCD"), id = 1 });

        Assert.Empty(_triggered);
    }

    /* An event with our signature but an id we never registered raises nothing */
    [Fact]
    public async Task HandleHotKey_UnknownId_IsIgnored()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());

        _carbon.DeliverHotKeyEvent(new IMacCarbonHotKeyApi.EventHotKeyID { signature = MoonTtsSignature, id = 42 });

        Assert.Empty(_triggered);
    }

    /* When the hotkey id can't be read from the event nothing is raised */
    [Fact]
    public async Task HandleHotKey_EventParameterUnreadable_IsIgnored()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());
        _carbon.FailGetEventParameter = true;

        _carbon.Press(KeyM, ControlKey);

        Assert.Empty(_triggered);
    }

    /* A combo replaced by re-registration no longer fires */
    [Fact]
    public async Task HandleHotKey_ReplacedCombo_IsNotDelivered()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "T"), new());

        int? status = _carbon.Press(KeyM, ControlKey);

        Assert.Null(status);
        Assert.Empty(_triggered);
    }

    /* Dispose removes the installed event handler */
    [Fact]
    public async Task Dispose_AfterInitialize_RemovesEventHandler()
    {
        await _hook.InitializeAsync();
        var handlerRef = _carbon.Handlers.Single().Ref;

        _hook.Dispose();

        Assert.Equal([handlerRef], _carbon.RemovedHandlerRefs);
        Assert.Empty(_carbon.Handlers);
    }

    /* Dispose unregisters every registered hotkey */
    [Fact]
    public async Task Dispose_WithHotKeys_UnregistersThem()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(
            new ShortcutCombo(true, false, false, false, "M"),
            new() { ["p"] = new ShortcutCombo(true, false, false, false, "P") });

        _hook.Dispose();

        Assert.Empty(_carbon.HotKeys);
    }

    /* Disposing twice removes the handler and the hotkeys only once */
    [Fact]
    public async Task Dispose_CalledTwice_ReleasesOnce()
    {
        await _hook.InitializeAsync();
        await _hook.RegisterShortcutAsync(new ShortcutCombo(true, false, false, false, "M"), new());

        _hook.Dispose();
        _hook.Dispose();

        Assert.Single(_carbon.RemovedHandlerRefs);
        Assert.Single(_carbon.UnregisteredRefs);
    }

    /* Disposing a hook that was never initialized touches nothing */
    [Fact]
    public void Dispose_WithoutInitialize_RemovesNothing()
    {
        _hook.Dispose();

        Assert.Empty(_carbon.RemovedHandlerRefs);
        Assert.Empty(_carbon.UnregisteredRefs);
    }
}
