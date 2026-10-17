using MoonTTS.Core.Services;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Constants;
using MoonTTS.Domain.Entities;

namespace MoonTTS.Core.Tests.Services;

/* Tests for HistoryManager. History audio lives in the config folder, so user folders are isolated */
[Collection(TestCollections.Environment)]
public sealed class HistoryManagerTests : IDisposable
{
    private static readonly AudioFormat Format = new(22050, 1, 16);

    private readonly UserFolderSandbox _sandbox = new();
    private readonly FakeConfigurationRepository _repo = new();
    private readonly FakeAudioPlayer _player = new();

    /* Restores the environment */
    public void Dispose() => _sandbox.Dispose();

    private string HistoryDirectory => Path.Combine(_sandbox.ConfigDirectory, "audio", "history");

    /* Builds a WAV holding `pcmLength` bytes of PCM */
    private static byte[] Wav(int pcmLength = 8) => WavWriter.CreateWav(new byte[pcmLength], Format);

    /* Stores an entry (and optionally its WAV file) in the repository, newest first */
    private HistoryEntry SeedEntry(bool withFile = true, byte[]? wav = null)
    {
        var id = Guid.NewGuid();
        var entry = new HistoryEntry { Id = id, Text = "seeded", CreatedAt = DateTime.Now, WavFileName = $"tts_output_{id}.wav" };
        if (withFile)
        {
            Directory.CreateDirectory(HistoryDirectory);
            File.WriteAllBytes(Path.Combine(HistoryDirectory, entry.WavFileName), wav ?? Wav());
        }

        _repo.SaveHistory([entry, .. _repo.HistoryEntries]);
        return entry;
    }

    /* The constructor creates the history audio folder */
    [Fact]
    public void Constructor_Always_CreatesHistoryDirectory()
    {
        _ = new HistoryManager(_player, _repo);

        Assert.True(Directory.Exists(HistoryDirectory));
    }

    /* A repository history change is forwarded as OnHistoryChanged */
    [Fact]
    public void Constructor_RepositoryHistoryChanged_RaisesOnHistoryChanged()
    {
        var manager = new HistoryManager(_player, _repo);
        int raised = 0;
        manager.OnHistoryChanged += () => raised++;

        _repo.SaveHistory([]);

        Assert.Equal(1, raised);
    }

    /* GetHistory returns the stored entries in order */
    [Fact]
    public void GetHistory_SeededEntries_ReturnsEntries()
    {
        var older = SeedEntry(withFile: false);
        var newer = SeedEntry(withFile: false);
        var manager = new HistoryManager(_player, _repo);

        var history = manager.GetHistory();

        Assert.Equal([newer.Id, older.Id], history.Select(h => h.Id));
    }

    /* GetHistory returns a snapshot that later repository changes do not alter */
    [Fact]
    public void GetHistory_Always_ReturnsCopy()
    {
        SeedEntry(withFile: false);
        var manager = new HistoryManager(_player, _repo);

        var history = manager.GetHistory();
        _repo.SaveHistory([]);

        Assert.Single(history);
    }

    /* Adding writes the WAV into the history folder under the entry's file name */
    [Fact]
    public async Task AddEntry_NewEntry_WritesWavFile()
    {
        var manager = new HistoryManager(_player, _repo);
        byte[] wav = Wav(16);

        var result = await manager.AddEntry("hello", wav);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(_repo.HistoryEntries);
        Assert.Equal($"tts_output_{entry.Id}.wav", entry.WavFileName);
        Assert.Equal(wav, await File.ReadAllBytesAsync(Path.Combine(HistoryDirectory, entry.WavFileName), TestContext.Current.CancellationToken));
    }

    /* Short texts are stored unchanged */
    [Fact]
    public async Task AddEntry_TextOf50Characters_IsStoredUnchanged()
    {
        var manager = new HistoryManager(_player, _repo);
        string text = new('a', 50);

        await manager.AddEntry(text, Wav());

        Assert.Equal(text, _repo.HistoryEntries[0].Text);
    }

    /* Texts over 50 characters are cut to 47 characters plus "..." */
    [Fact]
    public async Task AddEntry_TextOver50Characters_IsTruncatedWithEllipsis()
    {
        var manager = new HistoryManager(_player, _repo);

        await manager.AddEntry(new string('a', 51), Wav());

        Assert.Equal(new string('a', 47) + "...", _repo.HistoryEntries[0].Text);
    }

    /* New entries go to the top of the list and are saved once */
    [Fact]
    public async Task AddEntry_ExistingHistory_InsertsAtTop()
    {
        var existing = SeedEntry();
        int savesBefore = _repo.HistorySaveCount;
        var manager = new HistoryManager(_player, _repo);

        await manager.AddEntry("newest", Wav());

        Assert.Equal(2, _repo.HistoryEntries.Count);
        Assert.Equal("newest", _repo.HistoryEntries[0].Text);
        Assert.Equal(existing.Id, _repo.HistoryEntries[1].Id);
        Assert.Equal(savesBefore + 1, _repo.HistorySaveCount);
    }

    /* Going over the limit drops the oldest entry */
    [Fact]
    public async Task AddEntry_HistoryFull_RemovesOldestEntry()
    {
        var oldest = SeedEntry();
        for (int i = 1; i < AppConstants.MaxHistoryEntries; i++)
            SeedEntry();
        var manager = new HistoryManager(_player, _repo);

        await manager.AddEntry("newest", Wav());

        Assert.Equal(AppConstants.MaxHistoryEntries, _repo.HistoryEntries.Count);
        Assert.DoesNotContain(_repo.HistoryEntries, e => e.Id == oldest.Id);
        Assert.Equal("newest", _repo.HistoryEntries[0].Text);
    }

    /* The dropped oldest entry's WAV file is deleted from disk */
    [Fact]
    public async Task AddEntry_HistoryFull_DeletesOldestWavFile()
    {
        var oldest = SeedEntry();
        for (int i = 1; i < AppConstants.MaxHistoryEntries; i++)
            SeedEntry();
        var manager = new HistoryManager(_player, _repo);

        await manager.AddEntry("newest", Wav());

        Assert.False(File.Exists(Path.Combine(HistoryDirectory, oldest.WavFileName)));
    }

    /* Dropping an oldest entry whose file is already gone still succeeds */
    [Fact]
    public async Task AddEntry_HistoryFullWithMissingOldestFile_Succeeds()
    {
        SeedEntry(withFile: false);
        for (int i = 1; i < AppConstants.MaxHistoryEntries; i++)
            SeedEntry();
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.AddEntry("newest", Wav());

        Assert.True(result.IsSuccess);
        Assert.Equal(AppConstants.MaxHistoryEntries, _repo.HistoryEntries.Count);
    }

    /* Playing an unknown id fails without touching the player */
    [Fact]
    public async Task PlayEntryAsync_UnknownId_ReturnsFailure()
    {
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.PlayEntryAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* Playing an entry whose WAV is missing on disk fails */
    [Fact]
    public async Task PlayEntryAsync_MissingFile_ReturnsFailure()
    {
        var entry = SeedEntry(withFile: false);
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.PlayEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* With both outputs disabled playback is skipped and reported as success */
    [Fact]
    public async Task PlayEntryAsync_AllOutputsDisabled_SucceedsWithoutPlaying()
    {
        var entry = SeedEntry();
        _repo.SaveAppConfig(new AppConfig { EnablePrimaryPlayback = false, EnableMonitorPlayback = false });
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.PlayEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* The PCM after the 44-byte header is played in the format read from the header */
    [Fact]
    public async Task PlayEntryAsync_ValidEntry_PlaysPcmWithHeaderFormat()
    {
        var format = new AudioFormat(44100, 2, 16);
        byte[] pcm = [1, 2, 3, 4];
        var entry = SeedEntry(wav: WavWriter.CreateWav(pcm, format));
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.PlayEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var played = Assert.Single(_player.PlayedBuffers);
        Assert.Equal(pcm, played.Pcm);
        Assert.Equal(format, played.Format);
    }

    /* A file no longer than a header is played whole instead of being sliced */
    [Fact]
    public async Task PlayEntryAsync_FileNotLongerThanHeader_PlaysWholeFile()
    {
        byte[] bytes = Enumerable.Range(0, WavWriter.HeaderSize).Select(i => (byte)i).ToArray();
        var entry = SeedEntry(wav: bytes);
        var manager = new HistoryManager(_player, _repo);

        await manager.PlayEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, Assert.Single(_player.PlayedBuffers).Pcm);
    }

    /* Enabled outputs get their configured device, disabled ones get none, and volumes are applied */
    [Fact]
    public async Task PlayEntryAsync_ValidEntry_RoutesDevicesAndVolumes()
    {
        var entry = SeedEntry();
        _repo.SaveAppConfig(new AppConfig
        {
            EnablePrimaryPlayback = false, PrimaryAudioDeviceId = "speakers", PrimaryVolume = 30,
            EnableMonitorPlayback = true, MonitorAudioDeviceId = "virtual-mic", MonitorVolume = 70
        });
        var manager = new HistoryManager(_player, _repo);

        await manager.PlayEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.Null(_player.PrimaryDeviceId);
        Assert.Equal("virtual-mic", _player.MonitorDeviceId);
        Assert.Equal(30, _player.PrimaryVolume);
        Assert.Equal(70, _player.MonitorVolume);
    }

    /* A player failure is returned to the caller */
    [Fact]
    public async Task PlayEntryAsync_PlayerFails_ReturnsPlayerFailure()
    {
        var entry = SeedEntry();
        _player.PlaybackResult = Result.Failure("device busy");
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.PlayEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("device busy", result.ErrorMessage);
    }

    /* A player that throws turns into a failure carrying the exception */
    [Fact]
    public async Task PlayEntryAsync_PlayerThrows_ReturnsFailureWithException()
    {
        var entry = SeedEntry();
        var crash = new InvalidOperationException("native crash");
        var manager = new HistoryManager(new Doubles.RejectingAudioPlayer { ThrowOnPlay = crash }, _repo);

        var result = await manager.PlayEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("native crash", result.ErrorMessage);
        Assert.Same(crash, result.Exception);
    }

    /* A cancelled token turns into a "canceled" failure instead of an exception */
    [Fact]
    public async Task PlayEntryAsync_CancelledToken_ReturnsCanceledFailure()
    {
        var entry = SeedEntry();
        var manager = new HistoryManager(_player, _repo);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await manager.PlayEntryAsync(entry.Id, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Contains("canceled", result.ErrorMessage);
        Assert.Empty(_player.PlayedBuffers);
    }

    /* Pause is forwarded to the player */
    [Fact]
    public void Pause_Always_PausesPlayer()
    {
        new HistoryManager(_player, _repo).Pause();

        Assert.Equal(1, _player.PauseCount);
    }

    /* Resume is forwarded to the player */
    [Fact]
    public void Resume_Always_ResumesPlayer()
    {
        new HistoryManager(_player, _repo).Resume();

        Assert.Equal(1, _player.ResumeCount);
    }

    /* Stop is forwarded to the player */
    [Fact]
    public void Stop_Always_StopsPlayer()
    {
        new HistoryManager(_player, _repo).Stop();

        Assert.Equal(1, _player.StopCount);
    }

    /* Deleting an unknown id fails without saving */
    [Fact]
    public async Task DeleteEntry_UnknownId_ReturnsFailure()
    {
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.DeleteEntry(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(0, _repo.HistorySaveCount);
    }

    /* Deleting removes only the matching entry */
    [Fact]
    public async Task DeleteEntry_KnownId_RemovesEntry()
    {
        var keep = SeedEntry();
        var remove = SeedEntry();
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.DeleteEntry(remove.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal([keep.Id], _repo.HistoryEntries.Select(e => e.Id));
    }

    /* Deleting removes the entry's WAV file */
    [Fact]
    public async Task DeleteEntry_KnownId_DeletesWavFile()
    {
        var entry = SeedEntry();
        var manager = new HistoryManager(_player, _repo);

        await manager.DeleteEntry(entry.Id);

        Assert.False(File.Exists(Path.Combine(HistoryDirectory, entry.WavFileName)));
    }

    /* Deleting an entry whose file is already gone still succeeds */
    [Fact]
    public async Task DeleteEntry_MissingFile_Succeeds()
    {
        var entry = SeedEntry(withFile: false);
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.DeleteEntry(entry.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_repo.HistoryEntries);
    }

    /* Clearing empties the history and saves */
    [Fact]
    public async Task ClearHistory_WithEntries_EmptiesHistory()
    {
        SeedEntry();
        SeedEntry(withFile: false);
        int savesBefore = _repo.HistorySaveCount;
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.ClearHistory();

        Assert.True(result.IsSuccess);
        Assert.Empty(_repo.HistoryEntries);
        Assert.Equal(savesBefore + 1, _repo.HistorySaveCount);
    }

    /* Clearing deletes every entry's WAV file */
    [Fact]
    public async Task ClearHistory_WithEntries_DeletesWavFiles()
    {
        var first = SeedEntry();
        var second = SeedEntry();
        var manager = new HistoryManager(_player, _repo);

        await manager.ClearHistory();

        Assert.False(File.Exists(Path.Combine(HistoryDirectory, first.WavFileName)));
        Assert.False(File.Exists(Path.Combine(HistoryDirectory, second.WavFileName)));
    }

    /* Clearing an empty history still succeeds */
    [Fact]
    public async Task ClearHistory_EmptyHistory_Succeeds()
    {
        var manager = new HistoryManager(_player, _repo);

        var result = await manager.ClearHistory();

        Assert.True(result.IsSuccess);
    }
}
