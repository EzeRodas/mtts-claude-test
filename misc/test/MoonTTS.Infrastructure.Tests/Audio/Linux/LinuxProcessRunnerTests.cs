#if !WINDOWS
/* Linux audio types only exist outside the Windows build */
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoonTTS.Domain.Audio;
using MoonTTS.Infrastructure.Audio.Linux;

namespace MoonTTS.Infrastructure.Tests.Audio.Linux;

/* Verifies command execution, SIGSTOP/SIGCONT signalling and PCM piping against real dummy processes */
[Collection(TestCollections.Environment)]
[SupportedOSPlatform("linux")]
public sealed class LinuxProcessRunnerTests
{
    private static readonly AudioFormat Format = new(24000, 1, 16);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /* 16-bit little-endian PCM made of the given samples */
    private static byte[] Pcm(params short[] samples) => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

    /* `count` bytes of a recognisable ramp (even length = whole 16-bit samples) */
    private static byte[] Ramp(int count) => Enumerable.Range(0, count).Select(i => (byte)(i % 251)).ToArray();

    /* Yields the given chunks one by one, asynchronously */
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> Chunks(params byte[][] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    /* Yields one chunk, signals it, then waits forever (until cancelled) like a synthesizer still working */
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ChunkThenHang(
        byte[] chunk, TaskCompletionSource yielded, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return chunk;
        yielded.TrySetResult();
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
    }

    /* Starts a dummy player and wraps it as a stream target */
    private static LinuxProcessRunner.SinkStreamTarget StartTarget(string tool, Func<float>? volume = null)
    {
        var started = LinuxAudioStreamer.StartProcess(tool, "");
        Assert.True(started.IsSuccess, started.ErrorMessage);
        return new LinuxProcessRunner.SinkStreamTarget(started.Value.Process, started.Value.StandardInput, volume ?? (() => 1.0f));
    }

    /* Starts a long-running real process to receive signals */
    private static Process StartSleeper() =>
        Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false })!;

    /* Kills and releases a process started by a test */
    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            /* Never started or already released */
        }
        process.Dispose();
    }

    /* A successful command returns its stdout */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void RunCommand_ToolSucceeds_ReturnsStandardOutput()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("pactl", "Server Name: PulseAudio (on PipeWire 1.0.5)\n");

        var result = LinuxProcessRunner.RunCommand("pactl", "info");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Server Name: PulseAudio (on PipeWire 1.0.5)\n", result.Value);
    }

    /* Arguments are split and passed to the tool */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void RunCommand_WithArguments_PassesArgumentsToTool()
    {
        using var os = new LinuxDummyAudioSystem().AddTool("pactl");

        LinuxProcessRunner.RunCommand("pactl", "list sinks short");

        Assert.Equal(["list sinks short"], os.Tools.GetInvocations("pactl"));
    }

    /* A non-zero exit code is a failure carrying the command, exit code and stderr */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void RunCommand_ToolExitsWithError_ReturnsFailureWithExitCodeAndStderr()
    {
        using var os = new LinuxDummyAudioSystem().AddFailingTool("pactl", "Connection failure: Connection refused", exitCode: 3);

        var result = LinuxProcessRunner.RunCommand("pactl", "list sinks");

        Assert.True(result.IsFailure);
        Assert.Equal("'pactl list sinks' failed (exit 3): Connection failure: Connection refused", result.ErrorMessage);
    }

    /* A tool that is not installed is a failure carrying the exception, never a throw */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void RunCommand_ToolMissing_ReturnsFailureWithException()
    {
        using var os = new LinuxDummyAudioSystem();

        var result = LinuxProcessRunner.RunCommand("pactl", "list sinks");

        Assert.True(result.IsFailure);
        Assert.StartsWith("Failed to execute 'pactl': ", result.ErrorMessage);
        Assert.NotNull(result.Exception);
    }

    /* A tool that hangs after closing its output is killed once the timeout elapses */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task RunCommand_ToolHangs_ReturnsTimeoutFailureAndKillsTool()
    {
        using var os = new LinuxDummyAudioSystem();
        string pidFile = os.Temp.Combine("wpctl.pid");
        os.Tools.AddScript("wpctl", $"echo $$ > '{pidFile}'\nexec >&- 2>&-\nexec sleep 30");

        var result = LinuxProcessRunner.RunCommand("wpctl", "status", timeoutMs: 200);

        Assert.True(result.IsFailure);
        Assert.Equal("'wpctl' timed out after 200ms.", result.ErrorMessage);
        int pid = int.Parse(File.ReadAllText(pidFile).Trim());
        Assert.True(await LinuxProcessState.WaitForAsync(pid, s => s is null or 'Z' or 'X', Timeout, TestContext.Current.CancellationToken));
    }

    /* SIGSTOP really stops the target process */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SendPauseSignal_RunningProcess_StopsProcess()
    {
        var sleeper = StartSleeper();
        try
        {
            var result = LinuxProcessRunner.SendPauseSignal(sleeper.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(await LinuxProcessState.WaitForAsync(sleeper.Id, s => s == LinuxProcessState.Stopped, Timeout, TestContext.Current.CancellationToken));
        }
        finally
        {
            KillQuietly(sleeper);
        }
    }

    /* Signalling a pid that does not exist reports ESRCH (3) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void SendPauseSignal_ProcessDoesNotExist_ReturnsFailureWithErrno()
    {
        var result = LinuxProcessRunner.SendPauseSignal(int.MaxValue);

        Assert.True(result.IsFailure);
        Assert.Equal("kill -STOP failed: 3", result.ErrorMessage);
    }

    /* SIGCONT wakes a stopped process up again */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task SendResumeSignal_StoppedProcess_ContinuesProcess()
    {
        var sleeper = StartSleeper();
        try
        {
            LinuxProcessRunner.SendPauseSignal(sleeper.Id);
            Assert.True(await LinuxProcessState.WaitForAsync(sleeper.Id, s => s == LinuxProcessState.Stopped, Timeout, TestContext.Current.CancellationToken));

            var result = LinuxProcessRunner.SendResumeSignal(sleeper.Id);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(await LinuxProcessState.WaitForAsync(sleeper.Id, s => s is not null and not LinuxProcessState.Stopped, Timeout, TestContext.Current.CancellationToken));
        }
        finally
        {
            KillQuietly(sleeper);
        }
    }

    /* Resuming a pid that does not exist reports ESRCH (3) */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public void SendResumeSignal_ProcessDoesNotExist_ReturnsFailureWithErrno()
    {
        var result = LinuxProcessRunner.SendResumeSignal(int.MaxValue);

        Assert.True(result.IsFailure);
        Assert.Equal("kill -CONT failed: 3", result.ErrorMessage);
    }

    /* Without targets there is nothing to do and the chunks are not even read */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_NoTargets_SucceedsWithoutReadingChunks()
    {
        bool read = false;
        async IAsyncEnumerable<ReadOnlyMemory<byte>> Tracked()
        {
            read = true;
            await Task.Yield();
            yield return new byte[2];
        }

        var result = await LinuxProcessRunner.PipeStreamedAudioAsync([], Tracked(), Format, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(read);
    }

    /* Every chunk reaches the player byte for byte, including chunks larger than the 2400-byte write size */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_SingleTarget_WritesExactBytes()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");
        byte[] first = Ramp(5000), second = Ramp(122);
        var target = StartTarget("paplay");
        try
        {
            var result = await LinuxProcessRunner.PipeStreamedAudioAsync([target], Chunks(first, second), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal([.. first, .. second], os.GetPlayedBytes("paplay"));
        }
        finally
        {
            KillQuietly(target.Process);
        }
    }

    /* Empty chunks are skipped without breaking the stream */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_EmptyChunk_IsSkipped()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");
        var target = StartTarget("paplay");
        try
        {
            var result = await LinuxProcessRunner.PipeStreamedAudioAsync(
                [target], Chunks(Pcm(1, 2), [], Pcm(3)), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(Pcm(1, 2, 3), os.GetPlayedBytes("paplay"));
        }
        finally
        {
            KillQuietly(target.Process);
        }
    }

    /* Primary and monitor targets both receive the complete stream */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_TwoTargets_WritesSameBytesToBoth()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d").AddPlayer("pw-play", "--target");
        byte[] audio = Ramp(4800);
        var primary = StartTarget("paplay");
        var monitor = StartTarget("pw-play");
        try
        {
            var result = await LinuxProcessRunner.PipeStreamedAudioAsync([primary, monitor], Chunks(audio), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(audio, os.GetPlayedBytes("paplay"));
            Assert.Equal(audio, os.GetPlayedBytes("pw-play"));
        }
        finally
        {
            KillQuietly(primary.Process);
            KillQuietly(monitor.Process);
        }
    }

    /* Each target gets its own volume applied to its copy of the samples */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_DifferentVolumes_ScalesEachTargetIndependently()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d").AddPlayer("pw-play", "--target");
        var primary = StartTarget("paplay", () => 0.5f);
        var monitor = StartTarget("pw-play", () => 1.0f);
        try
        {
            var result = await LinuxProcessRunner.PipeStreamedAudioAsync(
                [primary, monitor], Chunks(Pcm(1000, -2000, 30000)), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(Pcm(500, -1000, 15000), os.GetPlayedBytes("paplay"));
            Assert.Equal(Pcm(1000, -2000, 30000), os.GetPlayedBytes("pw-play"));
        }
        finally
        {
            KillQuietly(primary.Process);
            KillQuietly(monitor.Process);
        }
    }

    /* The volume is read again for every write, so live volume changes apply mid-stream */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_VolumeChangesMidStream_AppliesNewVolumeToLaterWrites()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");
        float[] volumes = [1.0f, 0.0f];
        int writes = 0;
        var target = StartTarget("paplay", () => volumes[Math.Min(writes++, volumes.Length - 1)]);
        short[] samples = Enumerable.Repeat((short)1234, 2400).ToArray(); /* 4800 bytes = two 2400-byte writes */
        try
        {
            var result = await LinuxProcessRunner.PipeStreamedAudioAsync([target], Chunks(Pcm(samples)), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            byte[] played = os.GetPlayedBytes("paplay");
            Assert.Equal(Pcm(Enumerable.Repeat((short)1234, 1200).ToArray()), played[..2400]);
            Assert.Equal(new byte[2400], played[2400..]);
        }
        finally
        {
            KillQuietly(target.Process);
        }
    }

    /* A player that already exited is skipped while the others keep playing */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_TargetAlreadyExited_SkipsItAndFeedsOthers()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d").AddTool("pw-play", exitCode: 1);
        var crashed = StartTarget("pw-play");
        var alive = StartTarget("paplay");
        try
        {
            await crashed.Process.WaitForExitAsync(TestContext.Current.CancellationToken);

            var result = await LinuxProcessRunner.PipeStreamedAudioAsync([crashed, alive], Chunks(Pcm(7, 8, 9)), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(Pcm(7, 8, 9), os.GetPlayedBytes("paplay"));
        }
        finally
        {
            KillQuietly(crashed.Process);
            KillQuietly(alive.Process);
        }
    }

    /* Completion waits until the players have drained their buffers and exited */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_StreamEnds_ClosesStdinAndWaitsForPlayersToExit()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d", lingerSeconds: 0.3);
        var target = StartTarget("paplay");
        try
        {
            var result = await LinuxProcessRunner.PipeStreamedAudioAsync([target], Chunks(Pcm(1)), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(target.Process.HasExited);
        }
        finally
        {
            KillQuietly(target.Process);
        }
    }

    /* Cancelling while chunks are still being produced ends with a "Playback cancelled" failure */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_CancelledMidStream_ReturnsCancelledFailure()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var yielded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = StartTarget("paplay");
        try
        {
            var playing = LinuxProcessRunner.PipeStreamedAudioAsync([target], ChunkThenHang(Pcm(5, 6), yielded, TestContext.Current.CancellationToken), Format, cts.Token);
            await yielded.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            cts.Cancel();
            var result = await playing.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal("Playback cancelled", result.ErrorMessage);
        }
        finally
        {
            KillQuietly(target.Process);
        }
    }

    /* An already cancelled token fails immediately with "Playback cancelled" */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_AlreadyCancelled_ReturnsCancelledFailure()
    {
        using var os = new LinuxDummyAudioSystem().AddPlayer("paplay", "-d");
        var target = StartTarget("paplay");
        try
        {
            var result = await LinuxProcessRunner.PipeStreamedAudioAsync([target], Chunks(Pcm(1)), Format, new CancellationToken(canceled: true));

            Assert.True(result.IsFailure);
            Assert.Equal("Playback cancelled", result.ErrorMessage);
            Assert.Empty(os.GetPlayedBytes("paplay"));
        }
        finally
        {
            KillQuietly(target.Process);
        }
    }

    /* A broken player pipe becomes a "Streaming audio failed" failure instead of an exception */
    [Fact(Skip = "Linux only", SkipUnless = nameof(TestPlatform.IsLinux), SkipType = typeof(TestPlatform))]
    public async Task PipeStreamedAudioAsync_WriteFails_ReturnsStreamingFailure()
    {
        var sleeper = StartSleeper();
        var brokenPipe = new MemoryStream();
        brokenPipe.Dispose();
        try
        {
            var target = new LinuxProcessRunner.SinkStreamTarget(sleeper, brokenPipe, () => 1.0f);

            var result = await LinuxProcessRunner.PipeStreamedAudioAsync([target], Chunks(Pcm(1)), Format, TestContext.Current.CancellationToken);

            Assert.True(result.IsFailure);
            Assert.StartsWith("Streaming audio failed: ", result.ErrorMessage);
            Assert.IsType<ObjectDisposedException>(result.Exception);
        }
        finally
        {
            KillQuietly(sleeper);
        }
    }
}

#endif
