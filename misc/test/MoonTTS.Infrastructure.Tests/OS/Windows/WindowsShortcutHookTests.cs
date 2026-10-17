using System.Collections.Concurrent;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.OS.Windows;

namespace MoonTTS.Infrastructure.Tests.OS.Windows;

/* Drives WindowsShortcutHook against DummyWin32HotkeyApi, which answers like user32 */
public sealed class WindowsShortcutHookTests : IDisposable
{
    /* Win32 modifier flags as defined by the Windows SDK */
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmUser = 0x0400;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly DummyWin32HotkeyApi _win32 = new();
    private readonly WindowsShortcutHook _hook;
    private readonly ConcurrentQueue<string> _triggered = new();
    private readonly ConcurrentQueue<int> _triggerThreads = new();

    /* Every test gets a hook wired to a fresh dummy, recording what it reports */
    public WindowsShortcutHookTests()
    {
        _hook = new WindowsShortcutHook(_win32);
        _hook.OnShortcutTriggered += id =>
        {
            _triggered.Enqueue(id);
            _triggerThreads.Enqueue(_win32.GetCurrentThreadId());
        };
    }

    /* Stops the hook's message loop */
    public void Dispose() => _hook.Dispose();

    /* InitializeAsync returns only after the message-loop thread has created its message queue */
    [Fact]
    public async Task InitializeAsync_Called_CreatesMessageQueueBeforeReturning()
    {
        await _hook.InitializeAsync();

        Assert.NotEqual(0, _win32.MessageLoopThreadId);
        Assert.True(_win32.HasMessageQueue(_win32.MessageLoopThreadId));
    }

    /* The message loop runs on its own thread, not on the caller's */
    [Fact]
    public async Task InitializeAsync_Called_RunsMessageLoopOnSeparateThread()
    {
        await _hook.InitializeAsync();

        Assert.NotEqual(_win32.GetCurrentThreadId(), _win32.MessageLoopThreadId);
    }

    /* The message-loop thread is a background thread so it never keeps the app alive */
    [Fact]
    public async Task InitializeAsync_Called_UsesBackgroundThread()
    {
        await _hook.InitializeAsync();

        Assert.True(_win32.MessageLoopIsBackgroundThread);
    }

    /* The toggle shortcut is registered with id 1, MOD_NOREPEAT + its modifiers and the key's virtual-key code */
    [Fact]
    public async Task RegisterShortcutAsync_ToggleShortcut_RegistersCtrlAltM()
    {
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        var call = Assert.Single(_win32.RegisterCalls);
        Assert.Equal(new DummyWin32HotkeyApi.HotkeyCall(1, ModNoRepeat | ModControl | ModAlt, 0x4D, _win32.MessageLoopThreadId), call);
    }

    /* Hotkeys are registered on the message-loop thread, which is the thread Windows posts WM_HOTKEY to */
    [Fact]
    public async Task RegisterShortcutAsync_Called_RegistersOnMessageLoopThread()
    {
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(new ShortcutCombo(true, false, false, false, "K"), new());

        Assert.All(_win32.RegisterCalls, c => Assert.Equal(_win32.MessageLoopThreadId, c.ThreadId));
    }

    /* Each modifier of the combo maps to its Win32 MOD_* flag (always with MOD_NOREPEAT) */
    [Theory]
    [InlineData(true, false, false, false, ModControl)]
    [InlineData(false, true, false, false, ModAlt)]
    [InlineData(false, false, true, false, ModShift)]
    [InlineData(false, false, false, true, ModWin)]
    [InlineData(true, true, true, true, ModControl | ModAlt | ModShift | ModWin)]
    public async Task RegisterShortcutAsync_Modifiers_MapToWin32Flags(bool ctrl, bool alt, bool shift, bool win, uint expected)
    {
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(new ShortcutCombo(ctrl, alt, shift, win, "Q"), new());

        Assert.Equal(ModNoRepeat | expected, Assert.Single(_win32.RegisterCalls).Modifiers);
    }

    /* A lowercase key name is registered with the uppercase virtual-key code Win32 expects */
    [Fact]
    public async Task RegisterShortcutAsync_LowercaseKey_UsesUppercaseVirtualKey()
    {
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(new ShortcutCombo(true, false, false, false, "a"), new());

        Assert.Equal((uint)'A', Assert.Single(_win32.RegisterCalls).VirtualKey);
    }

    /* Digit keys use their ASCII code, which is also their Win32 virtual-key code (VK_0..VK_9) */
    [Fact]
    public async Task RegisterShortcutAsync_DigitKey_UsesDigitVirtualKey()
    {
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(new ShortcutCombo(true, false, false, false, "7"), new());

        Assert.Equal(0x37u, Assert.Single(_win32.RegisterCalls).VirtualKey);
    }

    /* Without a toggle shortcut only the phrase shortcuts are registered */
    [Fact]
    public async Task RegisterShortcutAsync_NullToggle_RegistersOnlyPhrases()
    {
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(null, new() { ["hello"] = new ShortcutCombo(true, false, false, false, "H") });

        var hotkey = Assert.Single(_win32.RegisteredHotkeys);
        Assert.Equal((uint)'H', hotkey.VirtualKey);
    }

    /* Toggle and phrase shortcuts get consecutive ids starting at 1 */
    [Fact]
    public async Task RegisterShortcutAsync_ToggleAndPhrases_UseConsecutiveIds()
    {
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(
            new ShortcutCombo(true, true, false, false, "M"),
            new() { ["a"] = new ShortcutCombo(true, false, false, false, "1"), ["b"] = new ShortcutCombo(true, false, false, false, "2") });

        Assert.Equal([1, 2, 3], _win32.RegisteredHotkeys.Select(h => h.Id).Order());
    }

    /* Registering again first releases every previously registered hotkey on the message-loop thread */
    [Fact]
    public async Task RegisterShortcutAsync_CalledAgain_UnregistersPreviousHotkeys()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(
            new ShortcutCombo(true, true, false, false, "M"),
            new() { ["a"] = new ShortcutCombo(true, false, false, false, "1") });

        await RegisterAndWaitAsync(new ShortcutCombo(false, true, false, false, "T"), new());

        Assert.Equal([1, 2], _win32.UnregisterCalls.Select(c => c.Id).Order());
        Assert.All(_win32.UnregisterCalls, c => Assert.Equal(_win32.MessageLoopThreadId, c.ThreadId));
    }

    /* After re-registration only the new set is active, numbered again from 1 */
    [Fact]
    public async Task RegisterShortcutAsync_CalledAgain_KeepsOnlyNewHotkeys()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        await RegisterAndWaitAsync(new ShortcutCombo(false, true, false, false, "T"), new());

        var hotkey = Assert.Single(_win32.RegisteredHotkeys);
        Assert.Equal(new DummyWin32HotkeyApi.RegisteredHotkey(1, ModNoRepeat | ModAlt, 'T', _win32.MessageLoopThreadId), hotkey);
    }

    /* An old combo no longer triggers anything after it was replaced */
    [Fact]
    public async Task RegisterShortcutAsync_CalledAgain_OldComboNoLongerFires()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());
        await RegisterAndWaitAsync(new ShortcutCombo(false, true, false, false, "T"), new());

        bool delivered = _win32.Press(ModControl | ModAlt, 'M');

        Assert.False(delivered);
        Assert.Empty(_triggered);
    }

    /* A combo Windows refuses (already held by another program) is skipped; the other shortcuts still work */
    [Fact]
    public async Task RegisterShortcutAsync_ComboTakenByAnotherApplication_OtherShortcutsStillFire()
    {
        _win32.ReserveForAnotherApplication(ModControl | ModAlt, 'M');
        await _hook.InitializeAsync();

        await RegisterAndWaitAsync(
            new ShortcutCombo(true, true, false, false, "M"),
            new() { ["greeting"] = new ShortcutCombo(true, true, false, false, "P") });
        PressAndWait(ModControl | ModAlt, 'P');

        Assert.Equal(["phrase_greeting"], _triggered);
    }

    /* A refused combo is not tracked, so it is never unregistered later */
    [Fact]
    public async Task RegisterShortcutAsync_ComboTakenByAnotherApplication_IsNotUnregisteredLater()
    {
        _win32.ReserveForAnotherApplication(ModControl | ModAlt, 'M');
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        await RegisterAndWaitAsync(null, new());

        Assert.Empty(_win32.UnregisterCalls);
    }

    /* When a phrase reuses the toggle combo Windows rejects the duplicate, so the press fires the toggle only once */
    [Fact]
    public async Task RegisterShortcutAsync_PhraseDuplicatesToggle_FiresToggleOnce()
    {
        await _hook.InitializeAsync();
        var combo = new ShortcutCombo(true, true, false, false, "M");

        await RegisterAndWaitAsync(combo, new() { ["dup"] = combo });
        PressAndWait(ModControl | ModAlt, 'M');

        Assert.Equal(["toggle_app"], _triggered);
    }

    /* Pressing the toggle combo raises OnShortcutTriggered with "toggle_app" */
    [Fact]
    public async Task EventLoop_TogglePressed_RaisesToggleApp()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        PressAndWait(ModControl | ModAlt, 'M');

        Assert.Equal(["toggle_app"], _triggered);
    }

    /* Pressing a phrase combo raises OnShortcutTriggered with "phrase_<id>" */
    [Fact]
    public async Task EventLoop_PhrasePressed_RaisesPhraseId()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(null, new() { ["thanks"] = new ShortcutCombo(false, false, true, false, "T") });

        PressAndWait(ModShift, 'T');

        Assert.Equal(["phrase_thanks"], _triggered);
    }

    /* OnShortcutTriggered is raised on the message-loop thread (consumers marshal to their UI thread) */
    [Fact]
    public async Task EventLoop_HotkeyPressed_RaisesEventOnMessageLoopThread()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        PressAndWait(ModControl | ModAlt, 'M');

        Assert.Equal([_win32.MessageLoopThreadId], _triggerThreads);
    }

    /* Each press is reported once, in order */
    [Fact]
    public async Task EventLoop_SeveralPresses_RaisesOneEventPerPress()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(
            new ShortcutCombo(true, true, false, false, "M"),
            new() { ["a"] = new ShortcutCombo(true, false, false, false, "1") });

        PressAndWait(ModControl, '1');
        PressAndWait(ModControl | ModAlt, 'M');
        PressAndWait(ModControl, '1');

        Assert.Equal(["phrase_a", "toggle_app", "phrase_a"], _triggered);
    }

    /* A WM_HOTKEY with an id the hook never registered is ignored and the loop keeps running */
    [Fact]
    public async Task EventLoop_UnknownHotkeyId_IsIgnored()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        _win32.PostThreadMessage(_win32.MessageLoopThreadId, DummyWin32HotkeyApi.WmHotkey, 99, IntPtr.Zero);
        PressAndWait(ModControl | ModAlt, 'M');

        Assert.Equal(["toggle_app"], _triggered);
    }

    /* Messages other than WM_HOTKEY / the wake-up message are ignored and the loop keeps running */
    [Fact]
    public async Task EventLoop_UnrelatedMessage_IsIgnored()
    {
        await _hook.InitializeAsync();
        await RegisterAndWaitAsync(new ShortcutCombo(true, true, false, false, "M"), new());

        _win32.PostThreadMessage(_win32.MessageLoopThreadId, WmUser, 1, IntPtr.Zero);
        PressAndWait(ModControl | ModAlt, 'M');

        Assert.Equal(["toggle_app"], _triggered);
    }

    /* A press with no subscriber does not break the loop */
    [Fact]
    public async Task EventLoop_NoSubscriber_KeepsRunning()
    {
        var win32 = new DummyWin32HotkeyApi();
        using var hook = new WindowsShortcutHook(win32);
        await hook.InitializeAsync();
        await hook.RegisterShortcutAsync(new ShortcutCombo(true, true, false, false, "M"), new());
        Assert.True(win32.WaitUntilIdle(Timeout));

        Assert.True(win32.Press(ModControl | ModAlt, 'M'));

        Assert.True(win32.WaitUntilIdle(Timeout));
    }

    /* A GetMessage error (-1) ends the loop instead of spinning on it */
    [Fact]
    public async Task EventLoop_GetMessageFails_EndsLoop()
    {
        await _hook.InitializeAsync();

        _win32.FailNextGetMessage();

        Assert.True(_win32.WaitForLoopExit(Timeout));
    }

    /* Dispose posts WM_QUIT, which ends the message loop */
    [Fact]
    public async Task Dispose_AfterInitialize_EndsMessageLoop()
    {
        await _hook.InitializeAsync();

        _hook.Dispose();

        Assert.True(_win32.WaitForLoopExit(Timeout));
    }

    /* Disposing a hook that was never initialized does not throw */
    [Fact]
    public void Dispose_WithoutInitialize_DoesNotThrow()
    {
        var hook = new WindowsShortcutHook(new DummyWin32HotkeyApi());

        var error = Record.Exception(hook.Dispose);

        Assert.Null(error);
    }

    /* Queues a registration and waits until the message loop has applied it */
    private async Task RegisterAndWaitAsync(ShortcutCombo? toggle, Dictionary<string, ShortcutCombo> phrases)
    {
        await _hook.RegisterShortcutAsync(toggle, phrases);
        Assert.True(_win32.WaitUntilIdle(Timeout), "message loop did not process the registration");
    }

    /* Presses a combo and waits until the message loop has dispatched it */
    private void PressAndWait(uint modifiers, uint virtualKey)
    {
        Assert.True(_win32.Press(modifiers, virtualKey), "combo is not registered");
        Assert.True(_win32.WaitUntilIdle(Timeout), "message loop did not process the press");
    }
}
