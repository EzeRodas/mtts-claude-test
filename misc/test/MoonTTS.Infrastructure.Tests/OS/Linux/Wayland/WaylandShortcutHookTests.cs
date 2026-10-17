using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.OS.Linux.Wayland;

namespace MoonTTS.Infrastructure.Tests.OS.Linux.Wayland;

/* Verifies the Wayland global shortcut hook against a dummy XDG Desktop Portal on a private D-Bus session bus */
[Collection(TestCollections.Environment)]
public sealed class WaylandShortcutHookTests
{
    private const string NeedsDbus = "Needs dbus-daemon for the private session bus";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly ShortcutCombo CtrlAltM = new(ctrl: true, alt: true, shift: false, win: false, "M");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /* No phrase shortcuts */
    private static Dictionary<string, ShortcutCombo> NoPhrases => new();

    /* Starts the hook on the private bus and registers the toggle shortcut */
    private static async Task<WaylandShortcutHook> RegisteredHookAsync(ShortcutCombo? toggle, Dictionary<string, ShortcutCombo>? phrases = null)
    {
        var hook = new WaylandShortcutHook();
        await hook.InitializeAsync();
        await hook.RegisterShortcutAsync(toggle, phrases ?? NoPhrases).WaitAsync(Timeout, Token);
        return hook;
    }

    /* Initialization registers the app id with the host portal registry */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task InitializeAsync_PortalAvailable_RegistersAppIdWithRegistry()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = new WaylandShortcutHook();

        await hook.InitializeAsync();

        Assert.Equal(["com.moontts.app"], portal.RegisteredAppIds);
    }

    /* A registry error is only a warning: initialization completes and shortcuts still work */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task InitializeAsync_RegistryRejectsAppId_StillInitializes()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        portal.RejectRegister = true;
        using var hook = new WaylandShortcutHook();

        await hook.InitializeAsync();
        await hook.RegisterShortcutAsync(CtrlAltM, NoPhrases).WaitAsync(Timeout, Token);

        Assert.Single(portal.BindShortcutsCalls);
    }

    /* Registering before initializing is a usage error */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_NotInitialized_ThrowsInvalidOperationException()
    {
        /* Not disposed: the hook never connected, so it holds no resources */
        var hook = new WaylandShortcutHook();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => hook.RegisterShortcutAsync(CtrlAltM, NoPhrases));

        Assert.Equal("Wayland hook not initialized.", error.Message);
    }

    /* CreateSession is called with fresh request and session tokens */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_Initialized_CreatesSessionWithHandleTokens()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(CtrlAltM);

        var call = Assert.Single(portal.CreateSessionCalls);
        Assert.Matches("^moontts_req_[0-9a-f]{32}$", call.HandleToken);
        Assert.Matches("^moontts_sess_[0-9a-f]{32}$", call.SessionHandleToken);
    }

    /* BindShortcuts targets the session returned by CreateSession, with an empty parent window */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_SessionCreated_BindsShortcutsOnThatSession()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(CtrlAltM);

        var bind = Assert.Single(portal.BindShortcutsCalls);
        Assert.Equal(portal.CreateSessionCalls[0].SessionHandle, bind.SessionHandle);
        Assert.Equal("", bind.ParentWindow);
        Assert.Matches("^moontts_req_[0-9a-f]{32}$", bind.HandleToken);
        Assert.NotEqual(portal.CreateSessionCalls[0].HandleToken, bind.HandleToken);
    }

    /* The toggle shortcut is bound as "toggle_app" with its description and preferred trigger */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_ToggleShortcut_BindsToggleApp()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(CtrlAltM);

        Assert.Equal(
            [new PortalShortcut("toggle_app", "Show/Hide Moon-TTS", "CTRL+ALT+m")],
            portal.BindShortcutsCalls[0].Shortcuts);
    }

    /* Phrase shortcuts are bound as "phrase_<name>" after the toggle shortcut */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_PhraseShortcuts_BindsEachPhrase()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        var phrases = new Dictionary<string, ShortcutCombo>
        {
            ["greeting"] = new(ctrl: false, alt: false, shift: true, win: false, "F1"),
        };
        using var hook = await RegisteredHookAsync(CtrlAltM, phrases);

        Assert.Equal(
            [
                new PortalShortcut("toggle_app", "Show/Hide Moon-TTS", "CTRL+ALT+m"),
                new PortalShortcut("phrase_greeting", "Moon-TTS Phrase: greeting", "SHIFT+F1"),
            ],
            portal.BindShortcutsCalls[0].Shortcuts);
    }

    /* Without any shortcut a session is created but nothing is bound */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_NoShortcuts_CreatesSessionWithoutBinding()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(null);

        Assert.Single(portal.CreateSessionCalls);
        Assert.Empty(portal.BindShortcutsCalls);
    }

    /* Modifiers are written in portal order CTRL, ALT, SHIFT, LOGO; letters in lowercase */
    [Theory(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    [InlineData(true, false, false, false, "A", "CTRL+a")]
    [InlineData(false, true, false, false, "z", "ALT+z")]
    [InlineData(false, false, true, false, "Q", "SHIFT+q")]
    [InlineData(false, false, false, true, "E", "LOGO+e")]
    [InlineData(true, true, true, true, "7", "CTRL+ALT+SHIFT+LOGO+7")]
    public async Task RegisterShortcutAsync_Modifiers_FormatsPreferredTrigger(bool ctrl, bool alt, bool shift, bool win, string key, string expected)
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(new ShortcutCombo(ctrl, alt, shift, win, key));

        Assert.Equal(expected, portal.BindShortcutsCalls[0].Shortcuts[0].PreferredTrigger);
    }

    /* Named keys and their aliases become XKB keysym names */
    [Theory(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    [InlineData("Enter", "Return")]
    [InlineData("Return", "Return")]
    [InlineData("Esc", "Escape")]
    [InlineData("Escape", "Escape")]
    [InlineData("Space", "space")]
    [InlineData("Tab", "Tab")]
    [InlineData("Backspace", "BackSpace")]
    [InlineData("Delete", "Delete")]
    [InlineData("Del", "Delete")]
    [InlineData("Insert", "Insert")]
    [InlineData("Ins", "Insert")]
    [InlineData("Home", "Home")]
    [InlineData("End", "End")]
    [InlineData("PageUp", "Page_Up")]
    [InlineData("Page_Up", "Page_Up")]
    [InlineData("PgUp", "Page_Up")]
    [InlineData("PageDown", "Page_Down")]
    [InlineData("Page_Down", "Page_Down")]
    [InlineData("PgDn", "Page_Down")]
    [InlineData("Up", "Up")]
    [InlineData("ArrowUp", "Up")]
    [InlineData("Down", "Down")]
    [InlineData("ArrowDown", "Down")]
    [InlineData("Left", "Left")]
    [InlineData("ArrowLeft", "Left")]
    [InlineData("Right", "Right")]
    [InlineData("ArrowRight", "Right")]
    [InlineData("Plus", "plus")]
    [InlineData("Add", "plus")]
    [InlineData("Minus", "minus")]
    [InlineData("Subtract", "minus")]
    public async Task RegisterShortcutAsync_NamedKey_FormatsAsKeysymName(string key, string keysym)
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(new ShortcutCombo(ctrl: true, alt: false, shift: false, win: false, key));

        Assert.Equal($"CTRL+{keysym}", portal.BindShortcutsCalls[0].Shortcuts[0].PreferredTrigger);
    }

    /* Function keys keep their uppercase F-name */
    [Theory(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    [InlineData("f1", "F1")]
    [InlineData("F12", "F12")]
    [InlineData("F24", "F24")]
    public async Task RegisterShortcutAsync_FunctionKey_KeepsFunctionKeyName(string key, string keysym)
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(new ShortcutCombo(ctrl: false, alt: true, shift: false, win: false, key));

        Assert.Equal($"ALT+{keysym}", portal.BindShortcutsCalls[0].Shortcuts[0].PreferredTrigger);
    }

    /* Any other key name is passed through as given by ShortcutCombo */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_OtherKeyName_PassesKeyThrough()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(new ShortcutCombo(ctrl: true, alt: false, shift: false, win: false, "KP_1"));

        Assert.Equal("CTRL+KP_1", portal.BindShortcutsCalls[0].Shortcuts[0].PreferredTrigger);
    }

    /* A session handle sent as an object path is accepted as well as a string */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_SessionHandleAsObjectPath_BindsOnThatSession()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        portal.SessionHandleFormat = SessionHandleFormat.ObjectPath;
        using var hook = await RegisteredHookAsync(CtrlAltM);

        Assert.Equal(portal.CreateSessionCalls[0].SessionHandle, portal.BindShortcutsCalls[0].SessionHandle);
    }

    /* A different request path returned by the portal is only a warning: the standard path is still awaited */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_PortalReturnsOtherRequestPath_StillCompletes()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        portal.RequestPathOverride = "/org/freedesktop/portal/desktop/request/legacy/1";
        using var hook = await RegisteredHookAsync(CtrlAltM);

        Assert.Single(portal.BindShortcutsCalls);
    }

    /* A failed CreateSession request (e.g. the user denied it) surfaces as an error with the response code */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_CreateSessionDenied_ThrowsWithResponseCode()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        portal.CreateSessionResponse = 1;
        using var hook = new WaylandShortcutHook();
        await hook.InitializeAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => hook.RegisterShortcutAsync(CtrlAltM, NoPhrases).WaitAsync(Timeout, Token));

        Assert.Equal("Portal request failed with code: 1", error.Message);
        Assert.Empty(portal.BindShortcutsCalls);
    }

    /* A CreateSession response without a session handle is rejected */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_NoSessionHandleInResponse_Throws()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        portal.SessionHandleFormat = SessionHandleFormat.Missing;
        using var hook = new WaylandShortcutHook();
        await hook.InitializeAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => hook.RegisterShortcutAsync(CtrlAltM, NoPhrases).WaitAsync(Timeout, Token));

        Assert.Equal("Portal CreateSession did not return a session_handle.", error.Message);
    }

    /* A failed BindShortcuts request surfaces as an error with the response code */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_BindShortcutsFails_ThrowsWithResponseCode()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        portal.BindShortcutsResponse = 2;
        using var hook = new WaylandShortcutHook();
        await hook.InitializeAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => hook.RegisterShortcutAsync(CtrlAltM, NoPhrases).WaitAsync(Timeout, Token));

        Assert.Equal("Portal request failed with code: 2", error.Message);
    }

    /* A portal that never answers the request times out after 10 seconds */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_PortalNeverResponds_TimesOut()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        portal.AnswerCreateSession = false;
        using var hook = new WaylandShortcutHook();
        await hook.InitializeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hook.RegisterShortcutAsync(CtrlAltM, NoPhrases).WaitAsync(TimeSpan.FromSeconds(30), Token));
    }

    /* Registering again closes the previous portal session before creating a new one */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_CalledAgain_ClosesPreviousSession()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(CtrlAltM);
        string firstSession = portal.CreateSessionCalls[0].SessionHandle;

        await hook.RegisterShortcutAsync(CtrlAltM, NoPhrases).WaitAsync(Timeout, Token);

        Assert.Equal([firstSession], portal.ClosedSessions);
        Assert.Equal(2, portal.CreateSessionCalls.Count);
        Assert.Equal(portal.CreateSessionCalls[1].SessionHandle, portal.BindShortcutsCalls[1].SessionHandle);
    }

    /* A failing Session.Close is ignored and registration continues */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_CloseOfPreviousSessionFails_StillRegisters()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(CtrlAltM);
        portal.RejectClose = true;

        await hook.RegisterShortcutAsync(CtrlAltM, NoPhrases).WaitAsync(Timeout, Token);

        Assert.Single(portal.ClosedSessions);
        Assert.Equal(2, portal.BindShortcutsCalls.Count);
    }

    /* The first registration has no previous session to close */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task RegisterShortcutAsync_FirstRegistration_ClosesNothing()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(CtrlAltM);

        Assert.Empty(portal.ClosedSessions);
    }

    /* A portal Activated signal raises OnShortcutTriggered with the shortcut id */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task Activated_PortalSignalsShortcut_RaisesOnShortcutTriggered()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        using var hook = await RegisteredHookAsync(CtrlAltM);
        var triggered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.OnShortcutTriggered += id => triggered.TrySetResult(id);

        portal.EmitActivated(portal.CreateSessionCalls[0].SessionHandle, "toggle_app");

        Assert.Equal("toggle_app", await triggered.Task.WaitAsync(Timeout, Token));
    }

    /* Phrase activations are forwarded with their phrase id */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task Activated_PhraseShortcut_RaisesOnShortcutTriggeredWithPhraseId()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        var phrases = new Dictionary<string, ShortcutCombo> { ["hello"] = CtrlAltM };
        using var hook = await RegisteredHookAsync(null, phrases);
        var triggered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.OnShortcutTriggered += id => triggered.TrySetResult(id);

        portal.EmitActivated(portal.CreateSessionCalls[0].SessionHandle, "phrase_hello");

        Assert.Equal("phrase_hello", await triggered.Task.WaitAsync(Timeout, Token));
    }

    /* Dispose disconnects the hook from the session bus */
    [Fact(Skip = NeedsDbus, SkipUnless = nameof(TestPlatform.HasDbusDaemon), SkipType = typeof(TestPlatform))]
    public async Task Dispose_Initialized_DisconnectsFromBus()
    {
        using var bus = await PrivateSessionBus.StartAsync(Token);
        using var portal = await DummyDesktopPortal.StartAsync(Token);
        var hook = new WaylandShortcutHook();
        await hook.InitializeAsync();
        string client = Assert.Single(portal.ClientNames);
        Assert.True(await portal.IsConnectedAsync(client));

        hook.Dispose();

        var deadline = DateTime.UtcNow + Timeout;
        while (await portal.IsConnectedAsync(client) && DateTime.UtcNow < deadline)
            await Task.Delay(10, Token);
        Assert.False(await portal.IsConnectedAsync(client));
    }
}
