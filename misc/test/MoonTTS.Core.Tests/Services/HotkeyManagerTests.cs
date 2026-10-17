using MoonTTS.Core.Services;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Constants;
using MoonTTS.Domain.Entities;

namespace MoonTTS.Core.Tests.Services;

/* Tests for HotkeyManager. Hotkey audio lives in the config folder, so user folders are isolated */
[Collection(TestCollections.Environment)]
public sealed class HotkeyManagerTests : IDisposable
{
    private readonly UserFolderSandbox _sandbox = new();
    private readonly FakeConfigurationRepository _repo = new();
    private readonly FakeSpeechPipeline _pipeline = new();
    private readonly FakeAudioPlayer _player = new();

    /* Seeds the profile the real repository always guarantees ("Default") plus a second one */
    public HotkeyManagerTests()
    {
        _repo.SaveProfileData(new ProfileDataStore
        {
            ActiveProfileName = "Default",
            Profiles =
            [
                new HotkeyProfile { Name = "Default", FolderName = "Default" },
                new HotkeyProfile { Name = "Streaming", FolderName = "streaming-folder" }
            ]
        });
    }

    /* Restores the environment */
    public void Dispose() => _sandbox.Dispose();

    /* The WAV path of a hotkey inside a profile folder */
    private string WavPath(Guid id, string folder = "Default") =>
        Path.Combine(_sandbox.ConfigDirectory, "audio", "hotkeys", folder, $"{id}.wav");

    /* Builds the manager under test */
    private HotkeyManager CreateManager() => new(_pipeline, _player, _repo);

    /* Stores a hotkey (and optionally its WAV file) in the repository */
    private HotkeyEntry SeedHotkey(string hotkey = "Ctrl+1", string text = "hello", byte[]? wav = null, bool withFile = true)
    {
        var entry = new HotkeyEntry { Id = Guid.NewGuid(), Hotkey = hotkey, Text = text };
        if (withFile)
        {
            string path = WavPath(entry.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, wav ?? WavWriter.CreateWav(new byte[8], new AudioFormat(22050, 1, 16)));
        }

        _repo.SaveHotkeys([.. _repo.Hotkeys, entry]);
        return entry;
    }

    /* A repository hotkeys change is forwarded as OnHotkeysChanged */
    [Fact]
    public void Constructor_RepositoryHotkeysChanged_RaisesOnHotkeysChanged()
    {
        var manager = CreateManager();
        int raised = 0;
        manager.OnHotkeysChanged += () => raised++;

        _repo.SaveHotkeys([]);

        Assert.Equal(1, raised);
    }

    /* GetHotkeys exposes the repository's hotkeys */
    [Fact]
    public void GetHotkeys_SeededHotkeys_ReturnsRepositoryHotkeys()
    {
        var entry = SeedHotkey(withFile: false);
        var manager = CreateManager();

        Assert.Equal([entry.Id], manager.GetHotkeys().Select(h => h.Id));
    }

    /* The toggle hotkey comes from the app config */
    [Fact]
    public void GetToggleHotkey_Always_ReturnsConfiguredHotkey()
    {
        _repo.SaveAppConfig(new AppConfig { ToggleAppHotkey = "Ctrl+Shift+T" });
        var manager = CreateManager();

        Assert.Equal("Ctrl+Shift+T", manager.GetToggleHotkey());
    }

    /* A blank toggle hotkey is rejected without saving */
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AssignToggleHotkey_BlankHotkey_ReturnsFailure(string hotkey)
    {
        var manager = CreateManager();

        var result = manager.AssignToggleHotkey(hotkey);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _repo.AppConfigSaveCount);
    }

    /* A valid toggle hotkey is trimmed and saved in the app config */
    [Fact]
    public void AssignToggleHotkey_ValidHotkey_SavesTrimmedHotkey()
    {
        var manager = CreateManager();

        var result = manager.AssignToggleHotkey("  Ctrl+Alt+K  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Ctrl+Alt+K", _repo.AppConfig.ToggleAppHotkey);
        Assert.Equal(1, _repo.AppConfigSaveCount);
    }

    /* Assigning the toggle hotkey raises OnToggleHotkeyChanged with the trimmed value */
    [Fact]
    public void AssignToggleHotkey_ValidHotkey_RaisesOnToggleHotkeyChanged()
    {
        var manager = CreateManager();
        string? raised = null;
        manager.OnToggleHotkeyChanged += h => raised = h;

        manager.AssignToggleHotkey(" Ctrl+Alt+K ");

        Assert.Equal("Ctrl+Alt+K", raised);
    }

    /* Loading the toggle hotkey returns the configured value */
    [Fact]
    public void LoadToggleHotkey_Always_ReturnsConfiguredHotkey()
    {
        _repo.SaveAppConfig(new AppConfig { ToggleAppHotkey = "Meta+M" });
        var manager = CreateManager();

        Assert.Equal("Meta+M", manager.LoadToggleHotkey());
    }

    /* Loading the toggle hotkey announces it through OnToggleHotkeyChanged */
    [Fact]
    public void LoadToggleHotkey_Always_RaisesOnToggleHotkeyChanged()
    {
        _repo.SaveAppConfig(new AppConfig { ToggleAppHotkey = "Meta+M" });
        var manager = CreateManager();
        string? raised = null;
        manager.OnToggleHotkeyChanged += h => raised = h;

        manager.LoadToggleHotkey();

        Assert.Equal("Meta+M", raised);
    }

    /* A blank hotkey combination is rejected before synthesis */
    [Fact]
    public async Task AssignHotkeyAsync_BlankHotkey_ReturnsFailure()
    {
        var manager = CreateManager();

        var result = await manager.AssignHotkeyAsync(" ", "text");

        Assert.True(result.IsFailure);
        Assert.Empty(_pipeline.Requests);
    }

    /* A blank phrase is rejected before synthesis */
    [Fact]
    public async Task AssignHotkeyAsync_BlankText_ReturnsFailure()
    {
        var manager = CreateManager();

        var result = await manager.AssignHotkeyAsync("Ctrl+1", "");

        Assert.True(result.IsFailure);
        Assert.Empty(_pipeline.Requests);
    }

    /* New hotkeys are refused once the limit is reached */
    [Fact]
    public async Task AssignHotkeyAsync_LimitReachedForNewHotkey_ReturnsFailure()
    {
        for (int i = 0; i < AppConstants.MaxHotkeyEntries; i++)
            SeedHotkey($"Ctrl+F{i}", withFile: false);
        var manager = CreateManager();

        var result = await manager.AssignHotkeyAsync("Ctrl+Z", "one too many");

        Assert.True(result.IsFailure);
        Assert.Empty(_pipeline.Requests);
    }

    /* Re-assigning an existing hotkey is allowed even when the limit is reached */
    [Fact]
    public async Task AssignHotkeyAsync_LimitReachedForExistingHotkey_Succeeds()
    {
        for (int i = 0; i < AppConstants.MaxHotkeyEntries; i++)
            SeedHotkey($"Ctrl+F{i}", withFile: false);
        var manager = CreateManager();

        var result = await manager.AssignHotkeyAsync("Ctrl+F0", "new phrase");

        Assert.True(result.IsSuccess);
        Assert.Equal(AppConstants.MaxHotkeyEntries, _repo.Hotkeys.Count);
    }

    /* The phrase is rendered silently (no playback, no history) straight into the active profile's folder */
    [Fact]
    public async Task AssignHotkeyAsync_NewHotkey_RendersSilentlyIntoActiveProfileFolder()
    {
        var manager = CreateManager();

        await manager.AssignHotkeyAsync("Ctrl+1", "hello there");

        var (text, options) = Assert.Single(_pipeline.Requests);
        Assert.Equal("hello there", text);
        Assert.NotNull(options);
        Assert.False(options.EnablePlayback);
        Assert.False(options.RecordHistory);
        var entry = Assert.Single(_repo.Hotkeys);
        Assert.Equal(WavPath(entry.Id), options.ExportPath);
    }

    /* The export path follows the active profile's folder name */
    [Fact]
    public async Task AssignHotkeyAsync_OtherActiveProfile_ExportsIntoThatProfileFolder()
    {
        _repo.SetActiveProfile("Streaming");
        var manager = CreateManager();

        await manager.AssignHotkeyAsync("Ctrl+1", "hello");

        var entry = Assert.Single(_repo.Hotkeys);
        Assert.Equal(WavPath(entry.Id, "streaming-folder"), _pipeline.Requests[0].Options!.ExportPath);
    }

    /* An active profile name that matches no profile falls back to the first profile's folder */
    [Fact]
    public async Task AssignHotkeyAsync_UnknownActiveProfile_ExportsIntoFirstProfileFolder()
    {
        _repo.SetActiveProfile("Missing");
        var manager = CreateManager();

        await manager.AssignHotkeyAsync("Ctrl+1", "hello");

        var entry = Assert.Single(_repo.Hotkeys);
        Assert.Equal(WavPath(entry.Id, "Default"), _pipeline.Requests[0].Options!.ExportPath);
    }

    /* A new hotkey is saved with its combination and phrase after a successful render */
    [Fact]
    public async Task AssignHotkeyAsync_NewHotkey_SavesEntry()
    {
        var manager = CreateManager();

        var result = await manager.AssignHotkeyAsync("Ctrl+1", "hello");

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(_repo.Hotkeys);
        Assert.Equal("Ctrl+1", entry.Hotkey);
        Assert.Equal("hello", entry.Text);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    /* Re-assigning a combination (ignoring case) updates its phrase and keeps its id */
    [Fact]
    public async Task AssignHotkeyAsync_ExistingHotkey_UpdatesTextAndKeepsId()
    {
        var existing = SeedHotkey("Ctrl+A", "old", withFile: false);
        var manager = CreateManager();

        await manager.AssignHotkeyAsync("ctrl+a", "new");

        var entry = Assert.Single(_repo.Hotkeys);
        Assert.Equal(existing.Id, entry.Id);
        Assert.Equal("new", entry.Text);
        Assert.Equal(WavPath(existing.Id), _pipeline.Requests[0].Options!.ExportPath);
    }

    /* A failed render returns a failure and saves nothing */
    [Fact]
    public async Task AssignHotkeyAsync_SynthesisFails_ReturnsFailureWithoutSaving()
    {
        _pipeline.ResultToReturn = Result<SpeechResult>.Failure("engine offline");
        var manager = CreateManager();

        var result = await manager.AssignHotkeyAsync("Ctrl+1", "hello");

        Assert.True(result.IsFailure);
        Assert.Contains("engine offline", result.ErrorMessage);
        Assert.Empty(_repo.Hotkeys);
        Assert.Equal(0, _repo.HotkeysSaveCount);
    }

    /* Playing an unknown hotkey id fails */
    [Fact]
    public async Task PlayHotkeyAsync_UnknownId_ReturnsFailure()
    {
        var manager = CreateManager();

        var result = await manager.PlayHotkeyAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* Playing a hotkey whose WAV is missing on disk fails */
    [Fact]
    public async Task PlayHotkeyAsync_MissingFile_ReturnsFailure()
    {
        var entry = SeedHotkey(withFile: false);
        var manager = CreateManager();

        var result = await manager.PlayHotkeyAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* With both outputs disabled playback is skipped and reported as success */
    [Fact]
    public async Task PlayHotkeyAsync_AllOutputsDisabled_SucceedsWithoutPlaying()
    {
        var entry = SeedHotkey();
        _repo.SaveAppConfig(new AppConfig { EnablePrimaryPlayback = false, EnableMonitorPlayback = false });
        var manager = CreateManager();

        var result = await manager.PlayHotkeyAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* The PCM after the header is played in the format read from the header */
    [Fact]
    public async Task PlayHotkeyAsync_ValidEntry_PlaysPcmWithHeaderFormat()
    {
        var format = new AudioFormat(48000, 2, 16);
        byte[] pcm = [9, 8, 7, 6];
        var entry = SeedHotkey(wav: WavWriter.CreateWav(pcm, format));
        var manager = CreateManager();

        var result = await manager.PlayHotkeyAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var played = Assert.Single(_player.PlayedBuffers);
        Assert.Equal(pcm, played.Pcm);
        Assert.Equal(format, played.Format);
    }

    /* A file no longer than a header is played whole instead of being sliced */
    [Fact]
    public async Task PlayHotkeyAsync_FileNotLongerThanHeader_PlaysWholeFile()
    {
        byte[] bytes = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        var entry = SeedHotkey(wav: bytes);
        var manager = CreateManager();

        await manager.PlayHotkeyAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, Assert.Single(_player.PlayedBuffers).Pcm);
    }

    /* Enabled outputs get their configured device, disabled ones get none, and volumes are applied */
    [Fact]
    public async Task PlayHotkeyAsync_ValidEntry_RoutesDevicesAndVolumes()
    {
        var entry = SeedHotkey();
        _repo.SaveAppConfig(new AppConfig
        {
            EnablePrimaryPlayback = true, PrimaryAudioDeviceId = "speakers", PrimaryVolume = 55,
            EnableMonitorPlayback = false, MonitorAudioDeviceId = "virtual-mic", MonitorVolume = 15
        });
        var manager = CreateManager();

        await manager.PlayHotkeyAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.Equal("speakers", _player.PrimaryDeviceId);
        Assert.Null(_player.MonitorDeviceId);
        Assert.Equal(55, _player.PrimaryVolume);
        Assert.Equal(15, _player.MonitorVolume);
    }

    /* A player failure is returned to the caller */
    [Fact]
    public async Task PlayHotkeyAsync_PlayerFails_ReturnsPlayerFailure()
    {
        var entry = SeedHotkey();
        _player.PlaybackResult = Result.Failure("device busy");
        var manager = CreateManager();

        var result = await manager.PlayHotkeyAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("device busy", result.ErrorMessage);
    }

    /* A player that throws turns into a failure carrying the exception */
    [Fact]
    public async Task PlayHotkeyAsync_PlayerThrows_ReturnsFailureWithException()
    {
        var entry = SeedHotkey();
        var crash = new InvalidOperationException("native crash");
        var manager = new HotkeyManager(_pipeline, new Doubles.RejectingAudioPlayer { ThrowOnPlay = crash }, _repo);

        var result = await manager.PlayHotkeyAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("native crash", result.ErrorMessage);
        Assert.Same(crash, result.Exception);
    }

    /* Playing by an unassigned combination fails */
    [Fact]
    public async Task PlayHotkeyByStringAsync_UnassignedHotkey_ReturnsFailure()
    {
        var manager = CreateManager();

        var result = await manager.PlayHotkeyByStringAsync("Ctrl+9");

        Assert.True(result.IsFailure);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* Playing by combination matches ignoring case and plays that hotkey's audio */
    [Fact]
    public async Task PlayHotkeyByStringAsync_AssignedHotkeyDifferentCase_PlaysAudio()
    {
        byte[] pcm = [5, 5, 5, 5];
        SeedHotkey("Ctrl+Shift+P", wav: WavWriter.CreateWav(pcm, new AudioFormat(22050, 1, 16)));
        var manager = CreateManager();

        var result = await manager.PlayHotkeyByStringAsync("ctrl+shift+p");

        Assert.True(result.IsSuccess);
        Assert.Equal(pcm, Assert.Single(_player.PlayedBuffers).Pcm);
    }

    /* Deleting an unknown hotkey fails without saving */
    [Fact]
    public void DeleteHotkey_UnknownId_ReturnsFailure()
    {
        var manager = CreateManager();

        var result = manager.DeleteHotkey(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(0, _repo.HotkeysSaveCount);
    }

    /* Deleting removes only the matching hotkey and saves */
    [Fact]
    public void DeleteHotkey_KnownId_RemovesEntry()
    {
        var keep = SeedHotkey("Ctrl+1");
        var remove = SeedHotkey("Ctrl+2");
        var manager = CreateManager();

        var result = manager.DeleteHotkey(remove.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal([keep.Id], _repo.Hotkeys.Select(h => h.Id));
    }

    /* Deleting removes the hotkey's WAV file */
    [Fact]
    public void DeleteHotkey_KnownId_DeletesWavFile()
    {
        var entry = SeedHotkey();
        var manager = CreateManager();

        manager.DeleteHotkey(entry.Id);

        Assert.False(File.Exists(WavPath(entry.Id)));
    }

    /* Deleting a hotkey whose file is already gone still succeeds */
    [Fact]
    public void DeleteHotkey_MissingFile_Succeeds()
    {
        var entry = SeedHotkey(withFile: false);
        var manager = CreateManager();

        var result = manager.DeleteHotkey(entry.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_repo.Hotkeys);
    }
}
