using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.OS;
using MoonTTS.Infrastructure.OS.Linux.Wayland;
using MoonTTS.Infrastructure.OS.Linux.X11;
using MoonTTS.Infrastructure.OS.MacOS;
using MoonTTS.Infrastructure.OS.Windows;

namespace MoonTTS.Infrastructure.Tests.OS;

/* GlobalShortcutManager: hook selection per OS / display server and forwarding of shortcut events.
   Event tests use FakeGlobalShortcutHook; selection tests only build hooks, never start them.
   In the Environment collection because selection reads WAYLAND_DISPLAY */
[Collection(TestCollections.Environment)]
public sealed class GlobalShortcutManagerTests
{
    private static readonly ShortcutCombo Toggle = new(ctrl: true, alt: true, shift: false, win: false, "M");

    /* InitializeAsync starts the hook it was given */
    [Fact]
    public async Task InitializeAsync_ProvidedHook_InitializesIt()
    {
        var hook = new FakeGlobalShortcutHook();
        using var manager = new GlobalShortcutManager(hook);

        await manager.InitializeAsync();

        Assert.True(hook.IsInitialized);
    }

    /* Shortcuts reported by the hook are raised by the manager with their id */
    [Fact]
    public async Task OnShortcutTriggered_HookReportsShortcut_IsForwarded()
    {
        var hook = new FakeGlobalShortcutHook();
        using var manager = new GlobalShortcutManager(hook);
        var received = new List<string>();
        manager.OnShortcutTriggered += received.Add;
        await manager.InitializeAsync();

        hook.Trigger("toggle_app");
        hook.Trigger("phrase-1");

        Assert.Equal(["toggle_app", "phrase-1"], received);
    }

    /* A shortcut without subscribers is ignored */
    [Fact]
    public async Task OnShortcutTriggered_NoSubscriber_DoesNotThrow()
    {
        var hook = new FakeGlobalShortcutHook();
        using var manager = new GlobalShortcutManager(hook);
        await manager.InitializeAsync();

        Assert.Null(Record.Exception(() => hook.Trigger("toggle_app")));
    }

    /* Shortcuts reported before InitializeAsync are not forwarded (the hook is not wired yet) */
    [Fact]
    public void OnShortcutTriggered_BeforeInitialize_IsNotForwarded()
    {
        var hook = new FakeGlobalShortcutHook();
        using var manager = new GlobalShortcutManager(hook);
        var received = new List<string>();
        manager.OnShortcutTriggered += received.Add;

        hook.Trigger("toggle_app");

        Assert.Empty(received);
    }

    /* UpdateShortcutAsync passes the toggle and phrase shortcuts to the hook */
    [Fact]
    public async Task UpdateShortcutAsync_AfterInitialize_RegistersShortcutsOnHook()
    {
        var hook = new FakeGlobalShortcutHook();
        using var manager = new GlobalShortcutManager(hook);
        await manager.InitializeAsync();
        var phrases = new Dictionary<string, ShortcutCombo> { ["p1"] = new(ctrl: true, alt: false, shift: true, win: false, "1") };

        await manager.UpdateShortcutAsync(Toggle, phrases);

        var registration = Assert.Single(hook.Registrations);
        Assert.Same(Toggle, registration.Toggle);
        Assert.Equal(["p1"], registration.Phrases.Keys);
    }

    /* UpdateShortcutAsync before InitializeAsync is refused */
    [Fact]
    public async Task UpdateShortcutAsync_BeforeInitialize_ThrowsInvalidOperation()
    {
        var hook = new FakeGlobalShortcutHook();
        using var manager = new GlobalShortcutManager(hook);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateShortcutAsync(Toggle, new()));
        Assert.Empty(hook.Registrations);
    }

    /* Dispose disposes the hook in use */
    [Fact]
    public async Task Dispose_AfterInitialize_DisposesHook()
    {
        var hook = new FakeGlobalShortcutHook();
        var manager = new GlobalShortcutManager(hook);
        await manager.InitializeAsync();

        manager.Dispose();

        Assert.True(hook.IsDisposed);
    }

    /* Dispose before InitializeAsync has nothing to release */
    [Fact]
    public void Dispose_BeforeInitialize_DoesNotDisposeHook()
    {
        var hook = new FakeGlobalShortcutHook();
        var manager = new GlobalShortcutManager(hook);

        manager.Dispose();

        Assert.False(hook.IsDisposed);
    }

    /* On Linux with a Wayland display the portal-based Wayland hook is chosen */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void CreatePlatformHook_LinuxWithWaylandDisplay_ReturnsWaylandHook()
    {
        using var env = new EnvironmentScope().Set("WAYLAND_DISPLAY", "wayland-0");

        Assert.IsType<WaylandShortcutHook>(GlobalShortcutManager.CreatePlatformHook());
    }

    /* On Linux without a Wayland display the X11 hook is chosen */
    [Theory(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    [InlineData(null)]
    [InlineData("")]
    public void CreatePlatformHook_LinuxWithoutWaylandDisplay_ReturnsX11Hook(string? waylandDisplay)
    {
        using var env = new EnvironmentScope().Set("WAYLAND_DISPLAY", waylandDisplay);

        Assert.IsType<X11ShortcutHook>(GlobalShortcutManager.CreatePlatformHook());
    }

    /* On Windows the Win32 RegisterHotKey hook is chosen */
    [Fact(Skip = "Windows only", SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void CreatePlatformHook_Windows_ReturnsWindowsHook()
    {
        Assert.IsType<WindowsShortcutHook>(GlobalShortcutManager.CreatePlatformHook());
    }

    /* On macOS the Carbon hot key hook is chosen */
    [Fact(Skip = "macOS only", SkipUnless = nameof(TestPlatform.IsMacOS), SkipType = typeof(TestPlatform))]
    public void CreatePlatformHook_MacOS_ReturnsMacHook()
    {
        Assert.IsType<MacShortcutHook>(GlobalShortcutManager.CreatePlatformHook());
    }
}
