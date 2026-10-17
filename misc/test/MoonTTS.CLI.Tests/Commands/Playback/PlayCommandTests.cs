using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Infrastructure.Daemon.DTOs;

namespace MoonTTS.CLI.Tests.Commands.Playback;

/* PlayCommand first probes the daemon pipe. The test process uses a private pipe folder (CliTestEnvironment), so the
   user's real daemon is never reached; "daemon running" tests host the daemon in-process (CliDummyDaemon) */
[Collection(TestCollections.Environment)]
public sealed class PlayCommandTests : IDisposable
{
    /* 16 kHz mono 16-bit: 32000 bytes per second, so two 16000-byte chunks are exactly one second */
    private static readonly AudioFormat DaemonFormat = new(16000, 1, 16);

    private readonly CliTestHost _host = new();

    /* Sets distinct configured volumes so overrides are visible */
    public PlayCommandTests()
    {
        _host.Repository.AppConfig.PrimaryVolume = 70;
        _host.Repository.AppConfig.MonitorVolume = 30;
    }

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Starts the dummy daemon with a resident model producing one second of audio in two chunks */
    private static Task<CliDummyDaemon> StartDaemonAsync() =>
        CliDummyDaemon.StartAsync(configureEngine: engine =>
        {
            engine.OutputFormat = DaemonFormat;
            engine.ChunksPerRequest = 2;
            engine.ChunkSize = 16000;
        });

    /* Build creates "play" with the text argument and the volume, speed and output options */
    [Fact]
    public void Build_Always_CreatesPlayCommandWithArgumentAndOptions()
    {
        var command = PlayCommand.Build(_host.Services);

        Assert.Equal("play", command.Name);
        Assert.Equal("text", Assert.Single(command.Arguments).Name);
        var names = command.Options.SelectMany(o => o.Aliases.Append(o.Name)).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "-v", "--volume", "-p", "--primary-volume", "-m", "--monitor-volume", "-s", "--speed", "-o", "--output"
        }, names);
    }

    /* The text being synthesized is always echoed first */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_Always_PrintsSynthesizingBanner()
    {
        var run = await _host.RunAsync(PlayCommand.Build, "hello there");

        Assert.StartsWith("Synthesizing: hello there", run.Output);
    }

    /* Omitting the text is a parse error and nothing is spoken */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_MissingText_ReturnsParseError()
    {
        var run = await _host.RunAsync(PlayCommand.Build);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Empty(_host.Pipeline.Requests);
    }

    /* Without a daemon the text is spoken in process through the speech pipeline */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemon_SpeaksThroughPipeline()
    {
        var run = await _host.RunAsync(PlayCommand.Build, "hello there");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("hello there", Assert.Single(_host.Pipeline.Requests).Text);
        Assert.Empty(_host.AudioPlayer.StreamedChunks);
    }

    /* Without options the pipeline gets no overrides, so it uses the configured settings */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemonWithoutOptions_PassesNoOverrides()
    {
        await _host.RunAsync(PlayCommand.Build, "hello");

        var options = Assert.Single(_host.Pipeline.Requests).Options!;
        Assert.Null(options.Speed);
        Assert.Null(options.PrimaryVolume);
        Assert.Null(options.MonitorVolume);
        Assert.Null(options.ExportPath);
    }

    /* Speed and export path are forwarded to the pipeline */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemonWithSpeedAndOutput_PassesThemToPipeline()
    {
        string wavPath = _host.Temp.Combine("speech.wav");

        await _host.RunAsync(PlayCommand.Build, "hello", "--speed", "1.5", "--output", wavPath);

        var options = Assert.Single(_host.Pipeline.Requests).Options!;
        Assert.Equal(1.5f, options.Speed);
        Assert.Equal(wavPath, options.ExportPath);
    }

    /* -v sets the volume of both outputs */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemonWithVolume_AppliesToBothOutputs()
    {
        await _host.RunAsync(PlayCommand.Build, "hello", "-v", "40");

        var options = Assert.Single(_host.Pipeline.Requests).Options!;
        Assert.Equal(40, options.PrimaryVolume);
        Assert.Equal(40, options.MonitorVolume);
    }

    /* -p and -m override -v for their own output */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemonWithPerOutputVolumes_OverridesVolumeOption()
    {
        await _host.RunAsync(PlayCommand.Build, "hello", "-v", "40", "-p", "10", "-m", "20");

        var options = Assert.Single(_host.Pipeline.Requests).Options!;
        Assert.Equal(10, options.PrimaryVolume);
        Assert.Equal(20, options.MonitorVolume);
    }

    /* A successful in-process synthesis reports the audio duration */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemonSucceeds_PrintsDuration()
    {
        _host.Pipeline.ResultToReturn = Result<SpeechResult>.Success(new SpeechResult { AudioDuration = TimeSpan.FromSeconds(2.5) });

        var run = await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Contains($"Speech synthesized successfully ({2.5:F1}s)", run.Output);
    }

    /* A failing pipeline reports its error and exits with 1 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemonPipelineFails_ReturnsErrorWithMessage()
    {
        _host.Pipeline.ResultToReturn = Result<SpeechResult>.Failure("TTS model is not available or downloaded.");

        var run = await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: TTS model is not available or downloaded.", run.Output);
    }

    /* A failure without any message falls back to a generic error */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_NoDaemonPipelineFailsWithoutMessage_PrintsGenericError()
    {
        _host.Pipeline.ResultToReturn = default;

        var run = await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Playback failed.", run.Output);
    }

    /* With the daemon running its audio chunks are streamed to the player and the pipeline is not used */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunning_StreamsDaemonAudioToPlayer()
    {
        await using var daemon = await StartDaemonAsync();

        var run = await _host.RunAsync(PlayCommand.Build, "hello there");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(_host.Pipeline.Requests);
        Assert.Equal("hello there", Assert.Single(daemon.Engine.Requests).Text);
        Assert.Collection(_host.AudioPlayer.StreamedChunks,
            chunk => Assert.Equal(Enumerable.Repeat((byte)0, 16000), chunk),
            chunk => Assert.Equal(Enumerable.Repeat((byte)1, 16000), chunk));
    }

    /* The player is fed in the audio format announced by the daemon's engine */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunning_UsesDaemonAudioFormat()
    {
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal(DaemonFormat, _host.AudioPlayer.LastStreamFormat);
    }

    /* The speed option is sent to the daemon */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunningWithSpeed_SendsSpeedToDaemon()
    {
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello", "-s", "1.25");

        Assert.Equal(1.25f, Assert.Single(daemon.Engine.Requests).Speed);
    }

    /* Enabled outputs are routed to their configured devices */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunning_RoutesToEnabledDevices()
    {
        _host.Repository.AppConfig.PrimaryAudioDeviceId = "alsa.speakers";
        _host.Repository.AppConfig.EnableMonitorPlayback = true;
        _host.Repository.AppConfig.MonitorAudioDeviceId = "mtts_mic";
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal("alsa.speakers", _host.AudioPlayer.PrimaryDeviceId);
        Assert.Equal("mtts_mic", _host.AudioPlayer.MonitorDeviceId);
    }

    /* Disabled outputs get no device */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunningWithOutputsDisabled_ClearsDevices()
    {
        _host.Repository.AppConfig.EnablePrimaryPlayback = false;
        _host.Repository.AppConfig.PrimaryAudioDeviceId = "alsa.speakers";
        _host.Repository.AppConfig.EnableMonitorPlayback = false;
        _host.Repository.AppConfig.MonitorAudioDeviceId = "mtts_mic";
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Null(_host.AudioPlayer.PrimaryDeviceId);
        Assert.Null(_host.AudioPlayer.MonitorDeviceId);
    }

    /* Without volume options the configured volumes are applied */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunningWithoutVolumes_UsesConfiguredVolumes()
    {
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal(70, _host.AudioPlayer.PrimaryVolume);
        Assert.Equal(30, _host.AudioPlayer.MonitorVolume);
    }

    /* -v applies to both outputs */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunningWithVolume_AppliesToBothOutputs()
    {
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello", "--volume", "45");

        Assert.Equal(45, _host.AudioPlayer.PrimaryVolume);
        Assert.Equal(45, _host.AudioPlayer.MonitorVolume);
    }

    /* -p and -m override -v for their own output */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunningWithPerOutputVolumes_OverridesVolumeOption()
    {
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello", "-v", "45", "--primary-volume", "5", "--monitor-volume", "15");

        Assert.Equal(5, _host.AudioPlayer.PrimaryVolume);
        Assert.Equal(15, _host.AudioPlayer.MonitorVolume);
    }

    /* --output writes the streamed PCM as a WAV file in the daemon's format */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunningWithOutput_WritesWavFile()
    {
        string wavPath = _host.Temp.Combine("speech.wav");
        await using var daemon = await StartDaemonAsync();

        await _host.RunAsync(PlayCommand.Build, "hello", "-o", wavPath);

        byte[] wav = await File.ReadAllBytesAsync(wavPath, TestContext.Current.CancellationToken);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(44 + 32000, wav.Length);
        Assert.Equal(DaemonFormat.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(Enumerable.Repeat((byte)1, 16000), wav[(44 + 16000)..]);
    }

    /* No audio means no file is written even with --output */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunningWithOutputButNoAudio_DoesNotWriteFile()
    {
        string wavPath = _host.Temp.Combine("speech.wav");
        await using var daemon = await CliDummyDaemon.StartAsync(configureEngine: engine => engine.ChunksPerRequest = 0);

        var run = await _host.RunAsync(PlayCommand.Build, "hello", "-o", wavPath);

        Assert.Equal(0, run.ExitCode);
        Assert.False(File.Exists(wavPath));
    }

    /* A daemon playback reports the streamed duration */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonRunning_PrintsDaemonDuration()
    {
        await using var daemon = await StartDaemonAsync();

        var run = await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Contains($"Speech synthesized via daemon successfully ({1.0:F1}s)", run.Output);
    }

    /* A player failure while streaming daemon audio is reported and exits with 1 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonPlaybackFails_ReturnsErrorWithMessage()
    {
        _host.AudioPlayer.PlaybackResult = Result.Failure("paplay exited with code 1");
        await using var daemon = await StartDaemonAsync();

        var run = await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: paplay exited with code 1", run.Output);
        Assert.Empty(_host.Pipeline.Requests);
    }

    /* A daemon without a loaded model refuses the request and the command falls back to the pipeline */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonWithoutModel_FallsBackToPipeline()
    {
        await using var daemon = await CliDummyDaemon.StartAsync(withModel: false);

        var run = await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("hello", Assert.Single(_host.Pipeline.Requests).Text);
        Assert.Empty(_host.AudioPlayer.StreamedChunks);
    }

    /* A daemon response without an audio format is rejected and the command falls back to the pipeline */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Play_DaemonOmitsAudioFormat_FallsBackToPipeline()
    {
        await using var daemon = await CliDummyDaemon.StartScriptedAsync(_ => [CliDummyDaemon.Response(new DaemonResponse { Success = true })]);

        var run = await _host.RunAsync(PlayCommand.Build, "hello");

        Assert.Equal(0, run.ExitCode);
        Assert.Single(daemon.Requests);
        Assert.Single(_host.Pipeline.Requests);
    }
}
