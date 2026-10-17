using MoonTTS.Core.Services;
using MoonTTS.Core.Tests.Doubles;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Core.Tests.Services;

/* Tests for SpeechPipeline. HistoryManager stores audio in the config folder, so user folders are isolated */
[Collection(TestCollections.Environment)]
public sealed class SpeechPipelineTests : IDisposable
{
    /* A text longer than the 80-character splitting threshold, made of three sentences */
    private const string LongText =
        "This is the first sentence of the text. This is the second sentence of it! And is this the third one?";

    private readonly UserFolderSandbox _sandbox = new();
    private readonly FakeConfigurationRepository _repo = new();
    private readonly FakeAudioPlayer _player = new();
    private readonly FakeTtsEngine _engine = new() { OutputFormat = new AudioFormat(16000, 1, 16), ChunksPerRequest = 2, ChunkSize = 100 };

    /* Restores the environment */
    public void Dispose() => _sandbox.Dispose();

    /* Builds a pipeline around the given engine and player (defaults: the scripted FakeTtsEngine and FakeAudioPlayer) */
    private SpeechPipeline CreatePipeline(ITtsEngine? engine = null, IAudioPlayer? player = null) =>
        new(new StubTtsEngineProvider(engine ?? _engine),
            new DictionaryManager(_repo),
            new HistoryManager(player ?? _player, _repo),
            new SettingsManager(_repo),
            player ?? _player);

    /* Adds a dictionary entry straight to the repository */
    private void AddDictionaryEntry(string original, string spelling) =>
        _repo.SaveDictionary([.. _repo.DictionaryEntries, new DictionaryEntry { Original = original, Spelling = spelling }]);

    /* Blank text is rejected without asking the engine */
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SpeakAsync_BlankText_ReturnsFailure(string text)
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync(text, new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_engine.Requests);
    }

    /* An unavailable engine is rejected without synthesis */
    [Fact]
    public async Task SpeakAsync_EngineUnavailable_ReturnsFailure()
    {
        _engine.IsAvailable = false;
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(_engine.Requests);
    }

    /* With the dictionary enabled the engine receives the substituted text */
    [Fact]
    public async Task SpeakAsync_ApplyDictionary_SynthesizesReplacedText()
    {
        AddDictionaryEntry("gg", "good game");
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("gg all", new SpeechOptions { ApplyDictionary = true }, TestContext.Current.CancellationToken);

        Assert.Equal("good game all", Assert.Single(_engine.Requests).Text);
    }

    /* With the dictionary disabled the engine receives the raw text */
    [Fact]
    public async Task SpeakAsync_DictionaryDisabled_SynthesizesRawText()
    {
        AddDictionaryEntry("gg", "good game");
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("gg all", new SpeechOptions { ApplyDictionary = false }, TestContext.Current.CancellationToken);

        Assert.Equal("gg all", Assert.Single(_engine.Requests).Text);
    }

    /* Texts of up to 80 characters are synthesized in one request, even with several sentences */
    [Fact]
    public async Task SpeakAsync_TextUpTo80Characters_SynthesizesInOneRequest()
    {
        string text = "One. Two. Three. " + new string('x', 63);
        Assert.Equal(80, text.Length);
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync(text, new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(text, Assert.Single(_engine.Requests).Text);
    }

    /* Texts longer than 80 characters are synthesized sentence by sentence, in order */
    [Fact]
    public async Task SpeakAsync_TextOver80Characters_SynthesizesEachSentence()
    {
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync(LongText, new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(TextSplitter.SplitIntoSentences(LongText), _engine.Requests.Select(r => r.Text));
        Assert.Equal(3, _engine.Requests.Count);
    }

    /* The speed override reaches every engine request */
    [Fact]
    public async Task SpeakAsync_SpeedOverride_IsPassedToEngine()
    {
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync(LongText, new SpeechOptions { Speed = 1.25f }, TestContext.Current.CancellationToken);

        Assert.All(_engine.Requests, r => Assert.Equal(1.25f, r.Speed));
    }

    /* With playback enabled every chunk is streamed to the player in the engine's format */
    [Fact]
    public async Task SpeakAsync_PlaybackEnabled_StreamsChunksInEngineFormat()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions { EnablePlayback = true }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _player.StreamedChunks.Count);
        Assert.Equal(_engine.OutputFormat, _player.LastStreamFormat);
    }

    /* With playback enabled the configured devices are routed: enabled outputs get their id, disabled ones none */
    [Fact]
    public async Task SpeakAsync_PlaybackEnabled_RoutesConfiguredDevices()
    {
        _repo.SaveAppConfig(new AppConfig
        {
            EnablePrimaryPlayback = true, PrimaryAudioDeviceId = "speakers",
            EnableMonitorPlayback = false, MonitorAudioDeviceId = "virtual-mic"
        });
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("speakers", _player.PrimaryDeviceId);
        Assert.Null(_player.MonitorDeviceId);
    }

    /* Without overrides the configured volumes are applied */
    [Fact]
    public async Task SpeakAsync_NoVolumeOverrides_AppliesConfiguredVolumes()
    {
        _repo.SaveAppConfig(new AppConfig { PrimaryVolume = 40, MonitorVolume = 60 });
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(40, _player.PrimaryVolume);
        Assert.Equal(60, _player.MonitorVolume);
    }

    /* Volume overrides in the options win over the configured volumes */
    [Fact]
    public async Task SpeakAsync_VolumeOverrides_AppliesOverrides()
    {
        _repo.SaveAppConfig(new AppConfig { PrimaryVolume = 40, MonitorVolume = 60 });
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions { PrimaryVolume = 5, MonitorVolume = 95 }, TestContext.Current.CancellationToken);

        Assert.Equal(5, _player.PrimaryVolume);
        Assert.Equal(95, _player.MonitorVolume);
    }

    /* With playback disabled the player is neither configured nor streamed to */
    [Fact]
    public async Task SpeakAsync_PlaybackDisabled_DoesNotUsePlayer()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions { EnablePlayback = false }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(_player.StreamedChunks);
        Assert.Null(_player.LastStreamFormat);
        Assert.Null(_player.PrimaryVolume);
    }

    /* The result reports the total PCM produced and its duration in the engine's format */
    [Fact]
    public async Task SpeakAsync_Success_ReportsPcmBytesAndDuration()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync(LongText, new SpeechOptions(), TestContext.Current.CancellationToken);

        int expectedBytes = 3 * _engine.ChunksPerRequest * _engine.ChunkSize;
        Assert.Equal(expectedBytes, result.Value!.TotalPcmBytes);
        Assert.Equal(_engine.OutputFormat.GetDuration(expectedBytes), result.Value.AudioDuration);
    }

    /* The result is the same whether or not the audio is played */
    [Fact]
    public async Task SpeakAsync_PlaybackDisabled_StillReportsPcmBytes()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions { EnablePlayback = false }, TestContext.Current.CancellationToken);

        Assert.Equal(_engine.ChunksPerRequest * _engine.ChunkSize, result.Value!.TotalPcmBytes);
    }

    /* An engine that produces nothing yields an empty, zero-latency result */
    [Fact]
    public async Task SpeakAsync_EngineProducesNoAudio_ReportsEmptyResult()
    {
        _engine.ChunksPerRequest = 0;
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalPcmBytes);
        Assert.Equal(TimeSpan.Zero, result.Value.TimeToFirstSound);
    }

    /* Recording enabled in both the options and the settings stores one history entry with the raw text */
    [Fact]
    public async Task SpeakAsync_RecordHistoryAndSaveHistory_AddsHistoryEntry()
    {
        AddDictionaryEntry("gg", "good game");
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("gg all", new SpeechOptions { RecordHistory = true }, TestContext.Current.CancellationToken);

        Assert.Equal("gg all", Assert.Single(_repo.HistoryEntries).Text);
    }

    /* The history WAV holds a header in the engine's format followed by all the PCM */
    [Fact]
    public async Task SpeakAsync_RecordHistory_StoresCompleteWav()
    {
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(_repo.HistoryEntries);
        byte[] wav = await File.ReadAllBytesAsync(
            Path.Combine(_sandbox.ConfigDirectory, "audio", "history", entry.WavFileName), TestContext.Current.CancellationToken);
        Assert.Equal(_engine.OutputFormat, FormatReader.GetFormatFromWavHeader(wav));
        Assert.Equal(WavWriter.HeaderSize + _engine.ChunksPerRequest * _engine.ChunkSize, wav.Length);
    }

    /* Recording disabled in the options stores nothing */
    [Fact]
    public async Task SpeakAsync_RecordHistoryDisabled_DoesNotAddHistory()
    {
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions { RecordHistory = false }, TestContext.Current.CancellationToken);

        Assert.Empty(_repo.HistoryEntries);
    }

    /* History disabled in the settings stores nothing, even when the options ask for it */
    [Fact]
    public async Task SpeakAsync_SaveHistorySettingDisabled_DoesNotAddHistory()
    {
        _repo.SaveAppConfig(new AppConfig { SaveHistory = false });
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions { RecordHistory = true }, TestContext.Current.CancellationToken);

        Assert.Empty(_repo.HistoryEntries);
    }

    /* Silent results (no PCM) are not recorded */
    [Fact]
    public async Task SpeakAsync_NoAudio_DoesNotAddHistory()
    {
        _engine.ChunksPerRequest = 0;
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(_repo.HistoryEntries);
    }

    /* An export path receives the full WAV */
    [Fact]
    public async Task SpeakAsync_ExportPath_WritesWavFile()
    {
        string exportPath = _sandbox.Temp.Combine("export.wav");
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions { EnablePlayback = false, ExportPath = exportPath }, TestContext.Current.CancellationToken);

        byte[] wav = await File.ReadAllBytesAsync(exportPath, TestContext.Current.CancellationToken);
        Assert.Equal(_engine.OutputFormat, FormatReader.GetFormatFromWavHeader(wav));
        Assert.Equal(WavWriter.HeaderSize + _engine.ChunksPerRequest * _engine.ChunkSize, wav.Length);
    }

    /* Silent results (no PCM) are not exported */
    [Fact]
    public async Task SpeakAsync_ExportPathWithNoAudio_DoesNotWriteFile()
    {
        _engine.ChunksPerRequest = 0;
        string exportPath = _sandbox.Temp.Combine("export.wav");
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions { ExportPath = exportPath }, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(exportPath));
    }

    /* An export into a missing folder becomes a failure instead of an exception */
    [Fact]
    public async Task SpeakAsync_ExportPathInMissingFolder_ReturnsFailure()
    {
        string exportPath = _sandbox.Temp.Combine("missing", "export.wav");
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions { EnablePlayback = false, ExportPath = exportPath }, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Exception);
    }

    /* A player failure is returned, and nothing is recorded */
    [Fact]
    public async Task SpeakAsync_PlayerFails_ReturnsPlayerFailure()
    {
        _player.PlaybackResult = Result.Failure("device lost");
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("device lost", result.ErrorMessage);
        Assert.Empty(_repo.HistoryEntries);
    }

    /* A player that fails without reading the stream does not leave synthesis running in the background */
    [Fact]
    public async Task SpeakAsync_PlayerFailsWithoutReading_StopsSynthesis()
    {
        var engine = new ControllableTtsEngine { ChunksPerRequest = 10, HangAfterChunks = false };
        var player = new RejectingAudioPlayer { BeforeFailing = async ct => await engine.WaitForStreamStartAsync(ct) };
        var pipeline = CreatePipeline(engine, player);

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);
        await engine.WaitForStreamCloseAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(RejectingAudioPlayer.Error, result.ErrorMessage);
    }

    /* An engine error during playback becomes a failure and stops the player */
    [Fact]
    public async Task SpeakAsync_EngineThrowsDuringPlayback_ReturnsFailureAndStopsPlayer()
    {
        _engine.ThrowOnSynthesize = new InvalidOperationException("worker died");
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions { EnablePlayback = true }, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("worker died", result.ErrorMessage);
        Assert.Equal(1, _player.StopCount);
    }

    /* An engine error without playback also becomes a failure */
    [Fact]
    public async Task SpeakAsync_EngineThrowsWithoutPlayback_ReturnsFailure()
    {
        _engine.ThrowOnSynthesize = new InvalidOperationException("worker died");
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions { EnablePlayback = false }, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("worker died", result.ErrorMessage);
        Assert.Empty(_repo.HistoryEntries);
    }

    /* A token cancelled up front yields a "cancelled" failure and stops the player */
    [Fact]
    public async Task SpeakAsync_CancelledToken_ReturnsCancelledFailure()
    {
        var pipeline = CreatePipeline();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await pipeline.SpeakAsync("hello", new SpeechOptions(), cts.Token);

        Assert.True(result.IsFailure);
        Assert.Contains("cancelled", result.ErrorMessage);
        Assert.Equal(1, _player.StopCount);
        Assert.Empty(_repo.HistoryEntries);
    }

    /* Cancelling the caller's token mid-speech ends the request with a failure */
    [Fact]
    public async Task SpeakAsync_TokenCancelledWhileSpeaking_ReturnsFailure()
    {
        var engine = new ControllableTtsEngine();
        var pipeline = CreatePipeline(engine);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var speaking = pipeline.SpeakAsync("hello", new SpeechOptions(), cts.Token);
        await engine.WaitForStreamStartAsync(TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        var result = await speaking;

        Assert.True(result.IsFailure);
        Assert.False(pipeline.IsSpeaking);
    }

    /* While a request is running IsSpeaking is true */
    [Fact]
    public async Task IsSpeaking_WhileSpeaking_IsTrue()
    {
        var engine = new ControllableTtsEngine();
        var pipeline = CreatePipeline(engine);

        var speaking = pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);
        await engine.WaitForStreamStartAsync(TestContext.Current.CancellationToken);

        Assert.True(pipeline.IsSpeaking);
        pipeline.Stop();
        await speaking;
    }

    /* After a request finishes IsSpeaking is false again */
    [Fact]
    public async Task IsSpeaking_AfterCompletion_IsFalse()
    {
        var pipeline = CreatePipeline();

        await pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);

        Assert.False(pipeline.IsSpeaking);
        Assert.False(pipeline.IsPaused);
    }

    /* Stop cancels the running request, which returns a failure */
    [Fact]
    public async Task Stop_WhileSpeaking_CancelsRequest()
    {
        var engine = new ControllableTtsEngine();
        var pipeline = CreatePipeline(engine);

        var speaking = pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);
        await engine.WaitForStreamStartAsync(TestContext.Current.CancellationToken);
        pipeline.Stop();
        var result = await speaking;

        Assert.True(result.IsFailure);
        Assert.Empty(_repo.HistoryEntries);
    }

    /* Stop clears the speaking and paused flags immediately */
    [Fact]
    public async Task Stop_WhileSpeakingAndPaused_ClearsFlags()
    {
        var engine = new ControllableTtsEngine();
        var pipeline = CreatePipeline(engine);
        var speaking = pipeline.SpeakAsync("hello", new SpeechOptions(), TestContext.Current.CancellationToken);
        await engine.WaitForStreamStartAsync(TestContext.Current.CancellationToken);
        pipeline.Pause();

        pipeline.Stop();

        Assert.False(pipeline.IsSpeaking);
        Assert.False(pipeline.IsPaused);
        await speaking;
    }

    /* Stop always stops the player, even when nothing is speaking */
    [Fact]
    public void Stop_Idle_StopsPlayer()
    {
        var pipeline = CreatePipeline();

        pipeline.Stop();

        Assert.Equal(1, _player.StopCount);
        Assert.False(pipeline.IsSpeaking);
    }

    /* A new request cancels the one still running */
    [Fact]
    public async Task SpeakAsync_NewRequestWhileSpeaking_CancelsPreviousRequest()
    {
        var engine = new ControllableTtsEngine();
        var pipeline = CreatePipeline(engine);
        var first = pipeline.SpeakAsync("first", new SpeechOptions(), TestContext.Current.CancellationToken);
        await engine.WaitForStreamStartAsync(TestContext.Current.CancellationToken);

        var second = pipeline.SpeakAsync("second", new SpeechOptions(), TestContext.Current.CancellationToken);
        var firstResult = await first;

        Assert.True(firstResult.IsFailure);
        pipeline.Stop();
        await second;
    }

    /* The superseded request finishing does not clear the speaking state of the request that replaced it */
    [Fact]
    public async Task SpeakAsync_PreviousRequestEnds_KeepsNewRequestSpeaking()
    {
        var engine = new ControllableTtsEngine();
        var pipeline = CreatePipeline(engine);
        var first = pipeline.SpeakAsync("first", new SpeechOptions(), TestContext.Current.CancellationToken);
        await engine.WaitForStreamStartAsync(TestContext.Current.CancellationToken);

        var second = pipeline.SpeakAsync("second", new SpeechOptions(), TestContext.Current.CancellationToken);
        await first;
        await engine.WaitForStreamStartAsync(TestContext.Current.CancellationToken);

        Assert.True(pipeline.IsSpeaking);
        pipeline.Stop();
        await second;
    }

    /* Pause pauses the player and sets IsPaused */
    [Fact]
    public void Pause_Always_PausesPlayerAndSetsIsPaused()
    {
        var pipeline = CreatePipeline();

        pipeline.Pause();

        Assert.Equal(1, _player.PauseCount);
        Assert.True(pipeline.IsPaused);
    }

    /* Resume resumes the player and clears IsPaused */
    [Fact]
    public void Resume_AfterPause_ResumesPlayerAndClearsIsPaused()
    {
        var pipeline = CreatePipeline();
        pipeline.Pause();

        pipeline.Resume();

        Assert.Equal(1, _player.ResumeCount);
        Assert.False(pipeline.IsPaused);
    }

    /* The short overload uses the default options: dictionary applied, audio played and recorded */
    [Fact]
    public async Task SpeakAsync_DefaultOptionsOverload_PlaysRecordsAndAppliesDictionary()
    {
        AddDictionaryEntry("gg", "good game");
        var pipeline = CreatePipeline();

        var result = await pipeline.SpeakAsync("gg", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("good game", Assert.Single(_engine.Requests).Text);
        Assert.NotEmpty(_player.StreamedChunks);
        Assert.Single(_repo.HistoryEntries);
    }
}
