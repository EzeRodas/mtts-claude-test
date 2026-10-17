#if !WINDOWS
/* Linux audio types only exist outside the Windows build */
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoonTTS.Domain.Audio;
using MoonTTS.Infrastructure.Audio;
using MoonTTS.Infrastructure.Audio.Linux;

namespace MoonTTS.Infrastructure.Tests.Audio.Linux;

/* Verifies the Linux audio player end to end against a dummy PipeWire / PulseAudio installation: device listing,
   output routing, volume, exact PCM delivery and real SIGSTOP / SIGCONT / kill of the player processes */
[Collection(TestCollections.Environment)]
[SupportedOSPlatform("linux")]
public sealed class LinuxAudioPlayerTests
{
    private const string Paplay = "paplay";
    private static readonly AudioFormat Format = new(24000, 1, 16);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /* 16-bit little-endian PCM made of the given samples */
    private static byte[] Pcm(params short[] samples) => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

    /* Yields the given chunks one by one, asynchronously */
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> Chunks(params byte[][] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    /* Yields one chunk then keeps the stream open until cancelled, like a synthesizer still working */
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ChunkThenHang(
        byte[] chunk, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return chunk;
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
    }

    /* A dummy system with a paplay player */
    private static LinuxDummyAudioSystem WithPaplay() => new LinuxDummyAudioSystem().AddPlayer(Paplay, "-d");

    /* A dummy pactl answering the sink listings like pipewire-pulse (module loading succeeds) */
    private static LinuxDummyAudioSystem WithPactl(LinuxDummyAudioSystem os, string sinksShort, string sinksLong)
    {
        string shortFile = os.Temp.WriteFile("sinks-short.txt", sinksShort);
        string longFile = os.Temp.WriteFile("sinks-long.txt", sinksLong);
        os.Tools.AddScript("pactl",
            "case \"$*\" in\n" +
            $"  'list sinks short') cat '{shortFile}' ;;\n" +
            $"  'list sinks') cat '{longFile}' ;;\n" +
            "  load-module*) echo 536870913 ;;\n" +
            "esac\n");
        return os;
    }

    /* Starts a stream that keeps playing and waits until both requested players received the first chunk */
    private static async Task<Task<MoonTTS.Domain.Common.Result>> StartHangingStreamAsync(
        LinuxDummyAudioSystem os, LinuxAudioPlayer player, params string[] sinks)
    {
        byte[] chunk = Pcm(1, 2, 3);
        var playing = player.StreamPcmAsync(ChunkThenHang(chunk, Token), Format, Token);
        foreach (string sink in sinks)
            Assert.True(await os.WaitForPlayedBytesAsync(Paplay, sink, chunk.Length, Token), $"Sink '{sink}' never received audio.");
        return playing;
    }

    /* The system default device is always offered first */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetAvailableDevices_NoSoundTools_ReturnsOnlyDefaultDevice()
    {
        using var os = new LinuxDummyAudioSystem();

        var devices = new LinuxAudioPlayer().GetAvailableDevices();

        Assert.True(devices.IsSuccess, devices.ErrorMessage);
        var only = Assert.Single(devices.Value!);
        Assert.Equal(("-1", "Default system device"), (only.DeviceId, only.DeviceName));
    }

    /* Existing virtual microphone is listed right after the default device, then the hardware sinks */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetAvailableDevices_PactlWithVirtualMic_ListsDefaultMicThenSinks()
    {
        using var os = WithPactl(new LinuxDummyAudioSystem(), LinuxToolOutputs.PactlListSinksShortWithVirtualMic, LinuxToolOutputs.PactlListSinks);

        var devices = new LinuxAudioPlayer().GetAvailableDevices();

        Assert.Equal(
            [
                ("-1", "Default system device"),
                ("Moon-TTS_Sink", "Moon-TTS Virtual Microphone"),
                ("alsa_output.pci-0000_00_1f.3.analog-stereo", "Built-in Audio Analog Stereo"),
                ("alsa_output.usb-Logitech_G435-00.analog-stereo", "G435 Wireless Gaming Headset Analog Stereo"),
            ],
            devices.Value!.Select(d => (d.DeviceId, d.DeviceName)));
    }

    /* Listing devices creates the virtual microphone when it does not exist yet */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetAvailableDevices_VirtualMicMissing_CreatesAndListsIt()
    {
        using var os = WithPactl(new LinuxDummyAudioSystem(), LinuxToolOutputs.PactlListSinksShort, LinuxToolOutputs.PactlListSinks);

        var devices = new LinuxAudioPlayer().GetAvailableDevices();

        Assert.Contains(os.Tools.GetInvocations("pactl"), call => call.StartsWith("load-module module-null-sink"));
        Assert.Contains(devices.Value!, d => d.DeviceId == "Moon-TTS_Sink" && d.DeviceName == "Moon-TTS Virtual Microphone");
    }

    /* The virtual sink reported again by the sink listing is not duplicated */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetAvailableDevices_SinkListingContainsVirtualSink_ListsItOnce()
    {
        using var os = WithPactl(new LinuxDummyAudioSystem(), LinuxToolOutputs.PactlListSinksShortWithVirtualMic, LinuxToolOutputs.PactlListSinksWithVirtualMic);

        var devices = new LinuxAudioPlayer().GetAvailableDevices();

        var mic = Assert.Single(devices.Value!, d => d.DeviceId == "Moon-TTS_Sink");
        Assert.Equal("Moon-TTS Virtual Microphone", mic.DeviceName);
    }

    /* When the microphone cannot be created it is left out, and wpctl sinks are listed by node id */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void GetAvailableDevices_VirtualMicUnavailable_ListsDefaultAndWpctlSinks()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("wpctl", LinuxToolOutputs.WpctlStatus);

        var devices = new LinuxAudioPlayer().GetAvailableDevices();

        Assert.Equal(
            [
                ("-1", "Default system device"),
                ("48", "Built-in Audio Analog Stereo"),
                ("57", "G435 Wireless Gaming Headset Analog Stereo"),
            ],
            devices.Value!.Select(d => (d.DeviceId, d.DeviceName)));
    }

    /* Setting outputs always succeeds */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void SetOutputDevices_AnyIds_ReturnsSuccess()
    {
        var result = new LinuxAudioPlayer().SetOutputDevices("speakers", "Moon-TTS_Sink");

        Assert.True(result.IsSuccess);
    }

    /* A fresh player plays on the system default sink only */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task StreamPcmAsync_DefaultOutputs_PlaysOnDefaultSinkOnly()
    {
        using var os = WithPaplay();

        var result = await new LinuxAudioPlayer().StreamPcmAsync(Chunks(Pcm(1, 2)), Format, Token);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(["--raw --rate=24000 --channels=1 --latency-msec=50 --format=s16le /dev/stdin"], os.Tools.GetInvocations(Paplay));
    }

    /* Primary and monitor each get their own player on their own sink */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetOutputDevices_PrimaryAndMonitor_StartsOnePlayerPerSink()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();

        player.SetOutputDevices("speakers", "Moon-TTS_Sink");
        await player.StreamPcmAsync(Chunks(Pcm(1)), Format, Token);

        var calls = os.Tools.GetInvocations(Paplay);
        Assert.Equal(2, calls.Count);
        Assert.EndsWith("-d speakers /dev/stdin", calls[0]);
        Assert.EndsWith("-d Moon-TTS_Sink /dev/stdin", calls[1]);
    }

    /* Leaving a parameter at its default keeps the previously configured device */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetOutputDevices_OnlyPrimaryGiven_KeepsCurrentMonitor()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices("speakers", "Moon-TTS_Sink");

        player.SetOutputDevices(primaryDeviceId: "headset");
        await player.StreamPcmAsync(Chunks(Pcm(1)), Format, Token);

        var calls = os.Tools.GetInvocations(Paplay);
        Assert.Equal(2, calls.Count);
        Assert.EndsWith("-d headset /dev/stdin", calls[0]);
        Assert.EndsWith("-d Moon-TTS_Sink /dev/stdin", calls[1]);
    }

    /* A monitor set to "-1" plays on the system default sink */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetOutputDevices_MonitorDefaultId_PlaysMonitorOnDefaultSink()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();

        player.SetOutputDevices("speakers", "-1");
        await player.StreamPcmAsync(Chunks(Pcm(4)), Format, Token);

        Assert.Equal(Pcm(4), os.GetPlayedBytes(Paplay, "speakers"));
        Assert.Equal(Pcm(4), os.GetPlayedBytes(Paplay, LinuxDummyAudioSystem.DefaultSink));
    }

    /* Null or blank ids disable an output; with both disabled nothing plays and the call succeeds */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetOutputDevices_BothBlankOrNull_PlaysNothingAndSucceeds()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();

        player.SetOutputDevices("   ", null);
        var result = await player.StreamPcmAsync(Chunks(Pcm(1)), Format, Token);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(os.Tools.WasInvoked(Paplay));
    }

    /* Disabling only the primary output keeps the monitor playing */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetOutputDevices_PrimaryDisabled_PlaysMonitorOnly()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();

        player.SetOutputDevices(null, "Moon-TTS_Sink");
        await player.StreamPcmAsync(Chunks(Pcm(9)), Format, Token);

        Assert.EndsWith("-d Moon-TTS_Sink /dev/stdin", Assert.Single(os.Tools.GetInvocations(Paplay)));
    }

    /* The primary volume scales only the primary copy of the samples */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetVolume_PrimaryHalf_ScalesPrimaryOnly()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices("speakers", "Moon-TTS_Sink");

        player.SetVolume(primaryVolume: 50);
        await player.StreamPcmAsync(Chunks(Pcm(1000, -4000)), Format, Token);

        Assert.Equal(Pcm(500, -2000), os.GetPlayedBytes(Paplay, "speakers"));
        Assert.Equal(Pcm(1000, -4000), os.GetPlayedBytes(Paplay, "Moon-TTS_Sink"));
    }

    /* The monitor volume scales only the monitor copy of the samples */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetVolume_MonitorQuarter_ScalesMonitorOnly()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices("speakers", "Moon-TTS_Sink");

        player.SetVolume(monitorVolume: 25);
        await player.StreamPcmAsync(Chunks(Pcm(1000, -4000)), Format, Token);

        Assert.Equal(Pcm(1000, -4000), os.GetPlayedBytes(Paplay, "speakers"));
        Assert.Equal(Pcm(250, -1000), os.GetPlayedBytes(Paplay, "Moon-TTS_Sink"));
    }

    /* A null volume leaves the previous value untouched */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetVolume_NullValue_KeepsPreviousVolume()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetVolume(primaryVolume: 50);

        player.SetVolume(primaryVolume: null, monitorVolume: 100);
        await player.StreamPcmAsync(Chunks(Pcm(1000)), Format, Token);

        Assert.Equal(Pcm(500), os.GetPlayedBytes(Paplay));
    }

    /* Volumes above 100 are clamped to 100 (samples untouched) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetVolume_AboveHundred_ClampsToFullVolume()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();

        player.SetVolume(primaryVolume: 150);
        await player.StreamPcmAsync(Chunks(Pcm(1000, -4000)), Format, Token);

        Assert.Equal(Pcm(1000, -4000), os.GetPlayedBytes(Paplay));
    }

    /* Negative volumes are clamped to 0 (silence) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SetVolume_Negative_ClampsToSilence()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();

        player.SetVolume(primaryVolume: -20);
        await player.StreamPcmAsync(Chunks(Pcm(1000, -4000)), Format, Token);

        Assert.Equal(Pcm(0, 0), os.GetPlayedBytes(Paplay));
    }

    /* A single buffer reaches primary and monitor byte for byte */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PlayPcmAsync_PrimaryAndMonitor_PipesExactBytesToBoth()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices("speakers", "Moon-TTS_Sink");
        byte[] pcm = Enumerable.Range(0, 6000).Select(i => (byte)(i * 7)).ToArray();

        var result = await player.PlayPcmAsync(pcm, Format, Token);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(pcm, os.GetPlayedBytes(Paplay, "speakers"));
        Assert.Equal(pcm, os.GetPlayedBytes(Paplay, "Moon-TTS_Sink"));
    }

    /* With every output disabled a buffer plays nowhere and still succeeds */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PlayPcmAsync_OutputsDisabled_SucceedsWithoutPlayer()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices(null, null);

        var result = await player.PlayPcmAsync(Pcm(1), Format, Token);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(os.Tools.WasInvoked(Paplay));
    }

    /* Streamed chunks arrive concatenated and in order */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task StreamPcmAsync_SeveralChunks_PipesThemInOrder()
    {
        using var os = WithPaplay();

        var result = await new LinuxAudioPlayer().StreamPcmAsync(Chunks(Pcm(1, 2), Pcm(3), Pcm(4, 5, 6)), Format, Token);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Pcm(1, 2, 3, 4, 5, 6), os.GetPlayedBytes(Paplay));
    }

    /* After a stream finishes no player process is left behind */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task StreamPcmAsync_Completed_LeavesNoPlayerRunning()
    {
        using var os = WithPaplay();

        await new LinuxAudioPlayer().StreamPcmAsync(Chunks(Pcm(1)), Format, Token);

        int pid = Assert.Single(os.GetPlayerPids(Paplay));
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s is null or 'Z' or 'X', Timeout, Token));
    }

    /* No player program installed is a failure */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task StreamPcmAsync_NoPlayerInstalled_ReturnsFailure()
    {
        using var os = new LinuxDummyAudioSystem();

        var result = await new LinuxAudioPlayer().StreamPcmAsync(Chunks(Pcm(1)), Format, Token);

        Assert.True(result.IsFailure);
        Assert.Equal("Failed to initialize any Linux audio output stream targets.", result.ErrorMessage);
    }

    /* pw-play is used when paplay is not installed */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task StreamPcmAsync_OnlyPwPlayInstalled_PlaysThroughPwPlay()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("pw-play", "--target");

        var result = await new LinuxAudioPlayer().StreamPcmAsync(Chunks(Pcm(3, 1)), Format, Token);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Pcm(3, 1), os.GetPlayedBytes("pw-play"));
    }

    /* Cancelling the caller's token stops playback, kills the players and reports cancellation */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task StreamPcmAsync_CallerCancels_KillsPlayersAndReturnsCancelled()
    {
        using var os = WithPaplay();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var player = new LinuxAudioPlayer();
        var playing = player.StreamPcmAsync(ChunkThenHang(Pcm(1), Token), Format, cts.Token);
        Assert.True(await os.WaitForPlayedBytesAsync(Paplay, LinuxDummyAudioSystem.DefaultSink, 2, Token));
        int pid = Assert.Single(os.GetPlayerPids(Paplay));

        cts.Cancel();
        var result = await playing.WaitAsync(Timeout, Token);

        Assert.Equal("Playback cancelled", result.ErrorMessage);
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s is null or 'Z' or 'X', Timeout, Token));
    }

    /* Starting a new stream stops the one still playing and the new one plays to completion */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task StreamPcmAsync_CalledWhilePlaying_StopsPreviousAndPlaysNewStream()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer(Paplay, "-d", lingerSeconds: 0.5);
        var player = new LinuxAudioPlayer();
        var first = await StartHangingStreamAsync(os, player, LinuxDummyAudioSystem.DefaultSink);
        int firstPid = Assert.Single(os.GetPlayerPids(Paplay));

        var second = await player.StreamPcmAsync(Chunks(Pcm(7, 7)), Format, Token);
        var firstResult = await first.WaitAsync(Timeout, Token);

        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(Pcm(7, 7), os.GetPlayedBytes(Paplay));
        Assert.Equal("Playback cancelled", firstResult.ErrorMessage);
        Assert.True(LinuxProcessState.IsGone(firstPid));
    }

    /* Pause sends SIGSTOP to every player (primary and monitor) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task Pause_WhilePlaying_StopsEveryPlayerProcess()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices("speakers", "Moon-TTS_Sink");
        var playing = await StartHangingStreamAsync(os, player, "speakers", "Moon-TTS_Sink");

        player.Pause();

        foreach (int pid in os.GetPlayerPids(Paplay))
            Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s == LinuxProcessState.Stopped, Timeout, Token), $"Player {pid} was not stopped.");
        player.Stop();
        await playing.WaitAsync(Timeout, Token);
    }

    /* A second Pause while already paused sends nothing (a player continued externally keeps running) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task Pause_AlreadyPaused_DoesNotSignalAgain()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        var playing = await StartHangingStreamAsync(os, player, LinuxDummyAudioSystem.DefaultSink);
        int pid = Assert.Single(os.GetPlayerPids(Paplay));
        player.Pause();
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s == LinuxProcessState.Stopped, Timeout, Token));
        LinuxProcessRunner.SendResumeSignal(pid);
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s is not null and not LinuxProcessState.Stopped, Timeout, Token));

        player.Pause();

        Assert.NotEqual(LinuxProcessState.Stopped, LinuxProcessState.Read(pid));
        player.Stop();
        await playing.WaitAsync(Timeout, Token);
    }

    /* Pausing with nothing playing is a harmless no-op */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Pause_NothingPlaying_DoesNotThrow()
    {
        var player = new LinuxAudioPlayer();

        var error = Record.Exception(player.Pause);

        Assert.Null(error);
    }

    /* Resume sends SIGCONT to every paused player */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task Resume_AfterPause_ContinuesEveryPlayerProcess()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices("speakers", "Moon-TTS_Sink");
        var playing = await StartHangingStreamAsync(os, player, "speakers", "Moon-TTS_Sink");
        player.Pause();
        foreach (int pid in os.GetPlayerPids(Paplay))
            Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s == LinuxProcessState.Stopped, Timeout, Token));

        player.Resume();

        foreach (int pid in os.GetPlayerPids(Paplay))
            Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s is not null and not LinuxProcessState.Stopped, Timeout, Token), $"Player {pid} was not continued.");
        player.Stop();
        await playing.WaitAsync(Timeout, Token);
    }

    /* Resume without a previous Pause sends nothing (a player stopped externally stays stopped) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task Resume_NotPaused_DoesNotSignal()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        var playing = await StartHangingStreamAsync(os, player, LinuxDummyAudioSystem.DefaultSink);
        int pid = Assert.Single(os.GetPlayerPids(Paplay));
        LinuxProcessRunner.SendPauseSignal(pid);
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s == LinuxProcessState.Stopped, Timeout, Token));

        player.Resume();

        Assert.Equal(LinuxProcessState.Stopped, LinuxProcessState.Read(pid));
        player.Stop();
        await playing.WaitAsync(Timeout, Token);
    }

    /* Stop kills every player and ends the running stream as cancelled */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task Stop_WhilePlaying_KillsPlayersAndCancelsStream()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        player.SetOutputDevices("speakers", "Moon-TTS_Sink");
        var playing = await StartHangingStreamAsync(os, player, "speakers", "Moon-TTS_Sink");

        player.Stop();
        var result = await playing.WaitAsync(Timeout, Token);

        Assert.Equal("Playback cancelled", result.ErrorMessage);
        foreach (int pid in os.GetPlayerPids(Paplay))
            Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s is null or 'Z' or 'X', Timeout, Token), $"Player {pid} is still alive.");
    }

    /* Stop also kills players that are paused (SIGSTOPped) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task Stop_WhilePaused_KillsStoppedPlayers()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        var playing = await StartHangingStreamAsync(os, player, LinuxDummyAudioSystem.DefaultSink);
        int pid = Assert.Single(os.GetPlayerPids(Paplay));
        player.Pause();
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s == LinuxProcessState.Stopped, Timeout, Token));

        player.Stop();

        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s is null or 'Z' or 'X', Timeout, Token));
        await playing.WaitAsync(Timeout, Token);
    }

    /* Stop clears the paused state, so a later Resume sends nothing */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task Stop_AfterPause_ResetsPausedState()
    {
        using var os = WithPaplay();
        var player = new LinuxAudioPlayer();
        var first = await StartHangingStreamAsync(os, player, LinuxDummyAudioSystem.DefaultSink);
        player.Pause();
        player.Stop();
        await first.WaitAsync(Timeout, Token);
        File.Delete(os.Tools.Directory + "/paplay.default.stdin");
        var second = await StartHangingStreamAsync(os, player, LinuxDummyAudioSystem.DefaultSink);
        int pid = os.GetPlayerPids(Paplay)[^1];
        LinuxProcessRunner.SendPauseSignal(pid);
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s == LinuxProcessState.Stopped, Timeout, Token));

        player.Resume();

        Assert.Equal(LinuxProcessState.Stopped, LinuxProcessState.Read(pid));
        player.Stop();
        await second.WaitAsync(Timeout, Token);
    }

    /* Stopping with nothing playing is a harmless no-op */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void Stop_NothingPlaying_DoesNotThrow()
    {
        var player = new LinuxAudioPlayer();

        var error = Record.Exception(player.Stop);

        Assert.Null(error);
    }
}

#endif
