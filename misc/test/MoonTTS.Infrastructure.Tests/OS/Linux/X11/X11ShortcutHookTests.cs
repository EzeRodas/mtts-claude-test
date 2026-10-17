using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.OS.Linux.X11;

namespace MoonTTS.Infrastructure.Tests.OS.Linux.X11;

/* Verifies the X11 global shortcut hook against a dummy X server behind IX11Api (no real display is opened) */
public sealed class X11ShortcutHookTests
{
    /* X11 protocol values (Xlib.h / X.h), spelled out so the hook's constants are checked too */
    private const uint ShiftMask = 0x01, LockMask = 0x02, ControlMask = 0x04, Mod1Mask = 0x08, Mod2Mask = 0x10, Mod4Mask = 0x40;
    private const uint KeycodeM = 58, KeycodeA = 38;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly ShortcutCombo CtrlAltM = new(ctrl: true, alt: true, shift: false, win: false, "M");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /* No phrase shortcuts */
    private static Dictionary<string, ShortcutCombo> NoPhrases => new();

    /* An initialized hook with Ctrl+Alt+M as toggle shortcut, recording every triggered id */
    private static async Task<(X11ShortcutHook Hook, List<string> Triggered)> HookWithToggleAsync(DummyX11Server server)
    {
        var hook = new X11ShortcutHook(server);
        var triggered = new List<string>();
        hook.OnShortcutTriggered += id => { lock (triggered) triggered.Add(id); };
        await hook.InitializeAsync();
        await hook.RegisterShortcutAsync(CtrlAltM, NoPhrases);
        return (hook, triggered);
    }

    /* Waits for the first shortcut raised by the hook */
    private static Task<string> NextTriggerAsync(X11ShortcutHook hook)
    {
        var next = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.OnShortcutTriggered += id => next.TrySetResult(id);
        return next.Task.WaitAsync(Timeout, Token);
    }

    /* Snapshot of the triggered ids */
    private static List<string> Snapshot(List<string> triggered)
    {
        lock (triggered) return triggered.ToList();
    }

    /* The parameterless constructor (production path, native libX11) makes no native call until initialized,
       so creating and disposing it never loads libX11 nor opens the real display */
    [Fact]
    public void Constructor_Parameterless_MakesNoNativeCallBeforeInitialize()
    {
        var hook = new X11ShortcutHook();

        var error = Record.Exception(hook.Dispose);

        Assert.Null(error);
    }

    /* Initialization opens the display named by $DISPLAY (null name) */
    [Fact]
    public async Task InitializeAsync_ServerAvailable_OpensDefaultDisplay()
    {
        var server = new DummyX11Server();
        using var hook = new X11ShortcutHook(server);

        await hook.InitializeAsync();

        Assert.Equal([null], server.OpenedDisplayNames);
    }

    /* No X server is a hard error */
    [Fact]
    public async Task InitializeAsync_ServerRefusesConnection_ThrowsInvalidOperationException()
    {
        var server = new DummyX11Server { RefuseConnection = true };
        using var hook = new X11ShortcutHook(server);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(hook.InitializeAsync);

        Assert.Equal("Failed to open x11 Display", error.Message);
    }

    /* Registering before initialization does nothing at all */
    [Fact]
    public async Task RegisterShortcutAsync_NotInitialized_MakesNoX11Calls()
    {
        var server = new DummyX11Server();
        using var hook = new X11ShortcutHook(server);

        await hook.RegisterShortcutAsync(CtrlAltM, NoPhrases);

        Assert.Equal(0, server.CallCount);
    }

    /* Registration first releases every previous grab (AnyKey, AnyModifier) on the root window */
    [Fact]
    public async Task RegisterShortcutAsync_Initialized_UngrabsAllKeysFirst()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        using var _hook = hook;

        Assert.Equal([new KeyUngrab(0, 0x8000, DummyX11Server.RootWindow)], server.Ungrabs);
    }

    /* The key is grabbed on the root window with every CapsLock / NumLock combination, asynchronously */
    [Fact]
    public async Task RegisterShortcutAsync_ToggleShortcut_GrabsKeyWithEveryLockVariant()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        using var _hook = hook;

        uint ctrlAlt = ControlMask | Mod1Mask;
        Assert.Equal(
            [
                new KeyGrab((int)KeycodeM, ctrlAlt, DummyX11Server.RootWindow, true, 1, 1),
                new KeyGrab((int)KeycodeM, ctrlAlt | LockMask, DummyX11Server.RootWindow, true, 1, 1),
                new KeyGrab((int)KeycodeM, ctrlAlt | Mod2Mask, DummyX11Server.RootWindow, true, 1, 1),
                new KeyGrab((int)KeycodeM, ctrlAlt | LockMask | Mod2Mask, DummyX11Server.RootWindow, true, 1, 1),
            ],
            server.Grabs);
    }

    /* Each modifier maps to its X11 mask: Ctrl=Control, Alt=Mod1, Shift=Shift, Win=Mod4 */
    [Theory]
    [InlineData(true, false, false, false, ControlMask)]
    [InlineData(false, true, false, false, Mod1Mask)]
    [InlineData(false, false, true, false, ShiftMask)]
    [InlineData(false, false, false, true, Mod4Mask)]
    [InlineData(true, true, true, true, ControlMask | Mod1Mask | ShiftMask | Mod4Mask)]
    public async Task RegisterShortcutAsync_Modifiers_GrabWithMatchingMask(bool ctrl, bool alt, bool shift, bool win, uint mask)
    {
        var server = new DummyX11Server();
        using var hook = new X11ShortcutHook(server);
        await hook.InitializeAsync();

        await hook.RegisterShortcutAsync(new ShortcutCombo(ctrl, alt, shift, win, "a"), NoPhrases);

        Assert.Equal(mask, server.Grabs[0].Modifiers);
    }

    /* ShortcutCombo stores keys uppercase; the keysym is looked up in lowercase as X11 names letters */
    [Fact]
    public async Task RegisterShortcutAsync_UppercaseKey_LooksUpLowercaseKeysym()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        using var _hook = hook;

        Assert.Equal(["m"], server.KeysymLookups);
    }

    /* An unknown keysym name grabs nothing */
    [Fact]
    public async Task RegisterShortcutAsync_UnknownKeysym_GrabsNothing()
    {
        var server = new DummyX11Server();
        using var hook = new X11ShortcutHook(server);
        await hook.InitializeAsync();

        await hook.RegisterShortcutAsync(new ShortcutCombo(ctrl: true, alt: false, shift: false, win: false, "NotAKey"), NoPhrases);

        Assert.Empty(server.Grabs);
    }

    /* A keysym the keyboard cannot produce (keycode 0) grabs nothing */
    [Fact]
    public async Task RegisterShortcutAsync_KeyNotOnKeyboard_GrabsNothing()
    {
        var server = new DummyX11Server().MapKey("q", 0x0071, keycode: 0);
        using var hook = new X11ShortcutHook(server);
        await hook.InitializeAsync();

        await hook.RegisterShortcutAsync(new ShortcutCombo(ctrl: true, alt: false, shift: false, win: false, "Q"), NoPhrases);

        Assert.Empty(server.Grabs);
    }

    /* Phrase shortcuts are grabbed too */
    [Fact]
    public async Task RegisterShortcutAsync_PhraseShortcuts_GrabsEveryPhraseKey()
    {
        var server = new DummyX11Server();
        using var hook = new X11ShortcutHook(server);
        await hook.InitializeAsync();
        var phrases = new Dictionary<string, ShortcutCombo>
        {
            ["hello"] = new(ctrl: false, alt: false, shift: true, win: false, "A"),
        };

        await hook.RegisterShortcutAsync(null, phrases);

        Assert.Equal(4, server.Grabs.Count);
        Assert.All(server.Grabs, g => Assert.Equal((int)KeycodeA, g.Keycode));
    }

    /* Registering again replaces the previous grabs */
    [Fact]
    public async Task RegisterShortcutAsync_CalledAgain_ReplacesPreviousGrabs()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        using var _hook = hook;

        await hook.RegisterShortcutAsync(new ShortcutCombo(ctrl: true, alt: false, shift: false, win: false, "A"), NoPhrases);

        Assert.Equal(2, server.Ungrabs.Count);
        Assert.All(server.Grabs, g => Assert.Equal((int)KeycodeA, g.Keycode));
    }

    /* A KeyPress of a grabbed combination raises OnShortcutTriggered with its id */
    [Fact]
    public async Task EventLoop_KeyPressOfRegisteredCombo_RaisesOnShortcutTriggered()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        using var _hook = hook;
        var next = NextTriggerAsync(hook);

        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask));

        Assert.Equal("toggle_app", await next);
    }

    /* The combination still triggers with CapsLock and/or NumLock active */
    [Theory]
    [InlineData(LockMask)]
    [InlineData(Mod2Mask)]
    [InlineData(LockMask | Mod2Mask)]
    public async Task EventLoop_KeyPressWithLockModifiers_RaisesOnShortcutTriggered(uint locks)
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        using var _hook = hook;
        var next = NextTriggerAsync(hook);

        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask | locks));

        Assert.Equal("toggle_app", await next);
    }

    /* Phrase combinations raise their phrase id */
    [Fact]
    public async Task EventLoop_KeyPressOfPhraseCombo_RaisesPhraseId()
    {
        var server = new DummyX11Server();
        using var hook = new X11ShortcutHook(server);
        await hook.InitializeAsync();
        await hook.RegisterShortcutAsync(null, new Dictionary<string, ShortcutCombo> { ["hello"] = CtrlAltM });
        var next = NextTriggerAsync(hook);

        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask));

        Assert.Equal("phrase_hello", await next);
    }

    /* A key that was not registered raises nothing */
    [Fact]
    public async Task EventLoop_KeyPressOfUnregisteredKey_DoesNotRaise()
    {
        var server = new DummyX11Server();
        var (hook, triggered) = await HookWithToggleAsync(server);
        using var _hook = hook;

        server.SendEvents((DummyX11Server.KeyPress, KeycodeA, ControlMask | Mod1Mask));
        await server.WaitUntilEventsProcessedAsync(Token);

        Assert.Empty(Snapshot(triggered));
    }

    /* Extra modifiers make it a different combination (Ctrl+Alt+Shift+M is not Ctrl+Alt+M) */
    [Fact]
    public async Task EventLoop_KeyPressWithExtraModifier_DoesNotRaise()
    {
        var server = new DummyX11Server();
        var (hook, triggered) = await HookWithToggleAsync(server);
        using var _hook = hook;

        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask | ShiftMask));
        await server.WaitUntilEventsProcessedAsync(Token);

        Assert.Empty(Snapshot(triggered));
    }

    /* Only KeyPress events count; releasing the keys raises nothing */
    [Fact]
    public async Task EventLoop_KeyReleaseOfRegisteredCombo_DoesNotRaise()
    {
        var server = new DummyX11Server();
        var (hook, triggered) = await HookWithToggleAsync(server);
        using var _hook = hook;

        server.SendEvents((DummyX11Server.KeyRelease, KeycodeM, ControlMask | Mod1Mask));
        await server.WaitUntilEventsProcessedAsync(Token);

        Assert.Empty(Snapshot(triggered));
    }

    /* Auto-repeat presses within 500 ms raise the shortcut only once */
    [Fact]
    public async Task EventLoop_RepeatedPressWithin500ms_RaisesOnce()
    {
        var server = new DummyX11Server();
        var (hook, triggered) = await HookWithToggleAsync(server);
        using var _hook = hook;

        server.SendEvents(
            (DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask),
            (DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask),
            (DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask));
        await server.WaitUntilEventsProcessedAsync(Token);

        Assert.Equal(["toggle_app"], Snapshot(triggered));
    }

    /* A new press after the 500 ms window raises the shortcut again */
    [Fact]
    public async Task EventLoop_PressAfter500ms_RaisesAgain()
    {
        var server = new DummyX11Server();
        var (hook, triggered) = await HookWithToggleAsync(server);
        using var _hook = hook;
        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask));
        await server.WaitUntilEventsProcessedAsync(Token);

        /* The debounce window is time based, so real time has to pass */
        await Task.Delay(600, Token);
        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask));
        await server.WaitUntilEventsProcessedAsync(Token);

        Assert.Equal(["toggle_app", "toggle_app"], Snapshot(triggered));
    }

    /* After re-registering, the old combination no longer raises anything */
    [Fact]
    public async Task EventLoop_OldComboAfterReRegistration_DoesNotRaise()
    {
        var server = new DummyX11Server();
        var (hook, triggered) = await HookWithToggleAsync(server);
        using var _hook = hook;
        await hook.RegisterShortcutAsync(new ShortcutCombo(ctrl: true, alt: false, shift: false, win: false, "A"), NoPhrases);

        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask));
        await server.WaitUntilEventsProcessedAsync(Token);

        Assert.Empty(Snapshot(triggered));
    }

    /* Dispose releases every grab and closes the display */
    [Fact]
    public async Task Dispose_Initialized_UngrabsAllKeysAndClosesDisplay()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);

        hook.Dispose();

        Assert.Empty(server.Grabs);
        Assert.Equal(new KeyUngrab(0, 0x8000, DummyX11Server.RootWindow), server.Ungrabs[^1]);
        Assert.True(server.IsClosed);
    }

    /* Dispose waits for the event loop to leave Xlib before closing the display (no use-after-close) */
    [Fact]
    public async Task Dispose_EventLoopInsideXlib_ClosesDisplayOnlyAfterLoopStops()
    {
        var server = new DummyX11Server();
        using var hook = new X11ShortcutHook(server);
        server.HoldEventLoop();
        await hook.InitializeAsync();
        Assert.True(server.WaitUntilEventLoopHeld());

        var disposing = Task.Run(hook.Dispose, Token);
        /* Gives a Dispose that does not wait for the loop the chance to close the display under it */
        await Task.WhenAny(server.Closed, Task.Delay(300, Token));
        Assert.True(server.ReleaseEventLoop());
        await disposing.WaitAsync(Timeout, Token);

        Assert.True(server.IsClosed);
        Assert.False(server.UsedAfterClose);
    }

    /* After Dispose the event loop no longer reads events */
    [Fact]
    public async Task Dispose_Initialized_StopsEventLoop()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        hook.Dispose();
        int calls = server.CallCount;

        server.SendEvents((DummyX11Server.KeyPress, KeycodeM, ControlMask | Mod1Mask));

        Assert.Equal(calls, server.CallCount);
        Assert.False(server.UsedAfterClose);
    }

    /* Disposing a hook that never connected makes no X11 calls */
    [Fact]
    public void Dispose_NotInitialized_MakesNoX11Calls()
    {
        var server = new DummyX11Server();
        var hook = new X11ShortcutHook(server);

        hook.Dispose();

        Assert.Equal(0, server.CallCount);
    }

    /* A second Dispose does nothing */
    [Fact]
    public async Task Dispose_CalledTwice_ClosesDisplayOnce()
    {
        var server = new DummyX11Server();
        var (hook, _) = await HookWithToggleAsync(server);
        hook.Dispose();
        int calls = server.CallCount;

        hook.Dispose();

        Assert.Equal(calls, server.CallCount);
    }
}
