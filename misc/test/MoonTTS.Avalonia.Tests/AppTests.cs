using System.Net.Http;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.Avalonia.Tests.TestSupport;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Infrastructure.OS;
using MoonTTS.Infrastructure.Persistence;
using MoonTTS.Infrastructure.Tts;
using MoonTTS.Core.Utilities;

namespace MoonTTS.Avalonia.Tests;

/* Verifies the application start-up that can run headless: XAML loading, DI registrations and the global
   shortcut wiring. Private start-up helpers have no public seam and are reached through GuiPrivate */
public sealed class AppTests
{
    /* Builds the service collection exactly as App does */
    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        GuiPrivate.InvokeStatic(typeof(App), "ConfigureServices", services);
        return services;
    }

    /* A GlobalShortcutManager whose OS hook is the dummy hook (InitializeAsync would create the real one) */
    private static GlobalShortcutManager ManagerWithHook(FakeGlobalShortcutHook hook)
    {
        var manager = new GlobalShortcutManager();
        GuiPrivate.SetField(manager, "_osHook", hook);
        return manager;
    }

    /* Every service App registers and the implementation it maps to (null for factory registrations) */
    public static TheoryData<Type, Type?> Registrations => new()
    {
        { typeof(IConfigurationRepository), typeof(JsonConfigurationRepository) },
        { typeof(SettingsManager), typeof(SettingsManager) },
        { typeof(DictionaryManager), typeof(DictionaryManager) },
        { typeof(HistoryManager), typeof(HistoryManager) },
        { typeof(ProfileManager), typeof(ProfileManager) },
        { typeof(HttpClient), typeof(HttpClient) },
        { typeof(GlobalShortcutManager), typeof(GlobalShortcutManager) },
        { typeof(IModelRegistry), null },
        { typeof(EngineVoiceStore), typeof(EngineVoiceStore) },
        { typeof(IModelDownloader), typeof(HttpModelDownloader) },
        { typeof(DownloadManager), typeof(DownloadManager) },
        { typeof(IAudioPlayer), null },
        { typeof(ITtsEngineFactory), typeof(TtsEngineFactory) },
        { typeof(ITtsEngineProvider), typeof(TtsEngineProvider) },
        { typeof(ISpeechPipeline), typeof(SpeechPipeline) },
        { typeof(HotkeyManager), typeof(HotkeyManager) },
    };

    /* ---------- Initialize / OnFrameworkInitializationCompleted ---------- */

    /* App.axaml registers the reflection-free view locator */
    [AvaloniaFact]
    public void Initialize_RegistersViewLocator()
    {
        Assert.Contains(Application.Current!.DataTemplates, template => template is ViewLocator);
    }

    /* App.axaml applies the Fluent theme */
    [AvaloniaFact]
    public void Initialize_AppliesFluentTheme()
    {
        Assert.Contains(Application.Current!.Styles, style => style is FluentTheme);
    }

    /* Without a desktop lifetime (headless, designer) no window or view model is created */
    [AvaloniaFact]
    public void OnFrameworkInitializationCompleted_NoDesktopLifetime_CreatesNoMainViewModel()
    {
        Assert.Null(Application.Current!.DataContext);
    }

    /* ---------- ConfigureServices ---------- */

    /* Each service is registered once, as a singleton shared by the whole GUI */
    [Theory]
    [MemberData(nameof(Registrations))]
    public void ConfigureServices_RegistersSingleton(Type service, Type? implementation)
    {
        var descriptor = Assert.Single(ConfigureServices(), d => d.ServiceType == service);

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        if (implementation != null)
            Assert.Equal(implementation, descriptor.ImplementationType);
    }

    /* The whole graph resolves without touching the OS (user folders are isolated by GuiIsolationFixture) */
    [Fact(Skip = "The audio player factory only supports POSIX in this build", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void ConfigureServices_EveryService_Resolves()
    {
        var registrations = ConfigureServices();
        using var provider = registrations.BuildServiceProvider();

        Assert.All(registrations, d => Assert.NotNull(provider.GetRequiredService(d.ServiceType)));
    }

    /* The model registry is the engine catalog scanning the installed engines */
    [Fact]
    public void ConfigureServices_ModelRegistry_IsEngineCatalog()
    {
        using var provider = ConfigureServices().BuildServiceProvider();

        Assert.IsType<EngineCatalog>(provider.GetRequiredService<IModelRegistry>());
    }

    /* The speech pipeline and the settings window share one engine provider (MoonTTS.md single-instance invariant) */
    [Fact]
    public void ConfigureServices_EngineProvider_IsSharedInstance()
    {
        using var provider = ConfigureServices().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<ITtsEngineProvider>(), provider.GetRequiredService<ITtsEngineProvider>());
    }

    /* ---------- Global shortcuts ---------- */

    /* The toggle hotkey and every phrase hotkey are registered with the OS hook, phrases keyed by entry id */
    [Fact]
    public async Task RefreshGlobalShortcutsAsync_ValidHotkeys_RegistersThem()
    {
        using var services = new GuiServices();
        services.Config.ToggleAppHotkey = "Ctrl+Alt+M";
        await services.Hotkeys.AssignHotkeyAsync("Ctrl+1", "hello");
        var phrase = services.Hotkeys.GetHotkeys().Single();
        var hook = new FakeGlobalShortcutHook();
        using var manager = ManagerWithHook(hook);

        await GuiPrivate.InvokeStaticAsync(typeof(App), "RefreshGlobalShortcutsAsync", manager, services.Hotkeys);

        var (toggle, phrases) = Assert.Single(hook.Registrations);
        Assert.Equal("Ctrl+Alt+M", toggle?.ToString());
        Assert.Equal("Ctrl+1", Assert.Single(phrases, p => p.Key == phrase.Id.ToString("D")).Value.ToString());
    }

    /* A toggle hotkey that cannot be parsed is registered as "no toggle shortcut" */
    [Fact]
    public async Task RefreshGlobalShortcutsAsync_InvalidToggleHotkey_RegistersNoToggle()
    {
        using var services = new GuiServices();
        services.Config.ToggleAppHotkey = "M";
        var hook = new FakeGlobalShortcutHook();
        using var manager = ManagerWithHook(hook);

        await GuiPrivate.InvokeStaticAsync(typeof(App), "RefreshGlobalShortcutsAsync", manager, services.Hotkeys);

        Assert.Null(Assert.Single(hook.Registrations).Toggle);
    }

    /* Phrase hotkeys that cannot be parsed are left out */
    [Fact]
    public async Task RefreshGlobalShortcutsAsync_InvalidPhraseHotkey_SkipsIt()
    {
        using var services = new GuiServices();
        await services.Hotkeys.AssignHotkeyAsync("1", "no modifier");
        var hook = new FakeGlobalShortcutHook();
        using var manager = ManagerWithHook(hook);

        await GuiPrivate.InvokeStaticAsync(typeof(App), "RefreshGlobalShortcutsAsync", manager, services.Hotkeys);

        Assert.Empty(Assert.Single(hook.Registrations).Phrases);
    }

    /* A hook that is not ready (no OS support) is reported, never thrown at the UI */
    [Fact]
    public async Task RefreshGlobalShortcutsAsync_HookNotInitialized_DoesNotThrow()
    {
        using var services = new GuiServices();
        using var manager = new GlobalShortcutManager();

        var error = await Record.ExceptionAsync(() =>
            GuiPrivate.InvokeStaticAsync(typeof(App), "RefreshGlobalShortcutsAsync", manager, services.Hotkeys));

        Assert.Null(error);
    }

    /* A pressed phrase shortcut plays the phrase's pre-rendered audio */
    [Fact]
    public async Task PlayPhraseShortcutAsync_KnownEntry_PlaysPhraseAudio()
    {
        using var services = new GuiServices();
        await services.Hotkeys.AssignHotkeyAsync("Ctrl+1", "hello");
        var phrase = services.Hotkeys.GetHotkeys().Single();
        string wav = services.HotkeyWavPath(phrase.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(wav)!);
        await File.WriteAllBytesAsync(wav, WavWriter.CreateWav(new byte[] { 9, 8, 7, 6 }, new AudioFormat(16000, 1, 16)), TestContext.Current.CancellationToken);

        await GuiPrivate.InvokeStaticAsync(typeof(App), "PlayPhraseShortcutAsync", services.Hotkeys, phrase.Id);

        Assert.Equal([9, 8, 7, 6], Assert.Single(services.Player.PlayedBuffers).Pcm);
    }

    /* A shortcut for a phrase that no longer exists plays nothing and does not throw */
    [Fact]
    public async Task PlayPhraseShortcutAsync_UnknownEntry_PlaysNothing()
    {
        using var services = new GuiServices();

        await GuiPrivate.InvokeStaticAsync(typeof(App), "PlayPhraseShortcutAsync", services.Hotkeys, Guid.NewGuid());

        Assert.Empty(services.Player.PlayedBuffers);
    }

    /* An exception inside the hotkey manager is caught (here: no hotkey profile to resolve the audio folder) */
    [Fact]
    public async Task PlayPhraseShortcutAsync_HotkeyManagerThrows_DoesNotThrow()
    {
        using var services = new GuiServices();
        var id = Guid.NewGuid();
        services.Repo.SaveHotkeys([new HotkeyEntry { Id = id, Hotkey = "Ctrl+1", Text = "hello" }]);
        services.Repo.SaveProfileData(new ProfileDataStore());

        var error = await Record.ExceptionAsync(() =>
            GuiPrivate.InvokeStaticAsync(typeof(App), "PlayPhraseShortcutAsync", services.Hotkeys, id));

        Assert.Null(error);
    }
}
