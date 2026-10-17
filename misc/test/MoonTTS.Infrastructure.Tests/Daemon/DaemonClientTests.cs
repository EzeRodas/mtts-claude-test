using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MoonTTS.Domain.Audio;
using MoonTTS.Infrastructure.Daemon;
using MoonTTS.Infrastructure.Daemon.DTOs;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.Infrastructure.Tests.Daemon;

/* DaemonClient over a real, isolated named pipe: against the in-process DaemonServer for real exchanges and against
   a scripted dummy daemon for replies the real server never sends. The daemon launcher is replaced so the test host
   is never spawned */
[Collection(TestCollections.Environment)]
public sealed class DaemonClientTests
{
    private const string PipesArePosixOnly = "Named pipes are machine wide on Windows and cannot be isolated per test";
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);
    private static readonly AudioFormat ScriptedFormat = new(16000, 2, 16);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* A client whose launcher fails the test if it is ever used */
    private static DaemonClient ClientThatMustNotLaunch() =>
        new(_ => throw new InvalidOperationException("The daemon must not be launched in this test."));

    /* Collects every chunk of a stream */
    private static async Task<List<byte[]>> CollectAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> chunks)
    {
        var collected = new List<byte[]>();
        await foreach (var chunk in chunks)
            collected.Add(chunk.ToArray());
        return collected;
    }

    /* Writes a Response frame */
    private static Task WriteResponseAsync(Stream stream, DaemonResponse response, CancellationToken ct) =>
        DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.Response,
            JsonSerializer.SerializeToUtf8Bytes(response, AppJsonContext.Default.DaemonResponse), ct).AsTask();

    /* IsRunningAsync is false when nothing listens on the pipe */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task IsRunningAsync_NoDaemon_ReturnsFalse()
    {
        using var pipe = new DaemonPipeScope();

        Assert.False(await ClientThatMustNotLaunch().IsRunningAsync());
    }

    /* IsRunningAsync is true while the daemon listens */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task IsRunningAsync_DaemonListening_ReturnsTrue()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        Assert.True(await ClientThatMustNotLaunch().IsRunningAsync());
    }

    /* EnsureRunningAsync succeeds without launching anything when the daemon already runs */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task EnsureRunningAsync_DaemonRunning_SucceedsWithoutLaunching()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().EnsureRunningAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    /* EnsureRunningAsync launches "<this executable> daemon run" and succeeds once the daemon listens */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task EnsureRunningAsync_DaemonNotRunning_LaunchesDaemonRunCommand()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        ProcessStartInfo? launched = null;
        var client = new DaemonClient(startInfo =>
        {
            launched = startInfo;
            _ = daemon.StartAsync(pipe, Ct);
            return null;
        });

        var result = await client.EnsureRunningAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(launched);
        Assert.Equal(Environment.ProcessPath ?? "mtts", launched.FileName);
        Assert.EndsWith("daemon run", launched.Arguments);
        Assert.False(launched.UseShellExecute);
        Assert.True(launched.CreateNoWindow);
    }

    /* A launcher failure yields a failure instead of an exception */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task EnsureRunningAsync_LaunchFails_ReturnsFailure()
    {
        using var pipe = new DaemonPipeScope();
        var client = new DaemonClient(_ => throw new System.ComponentModel.Win32Exception("no such file"));

        var result = await client.EnsureRunningAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("Failed to auto-spawn daemon", result.ErrorMessage);
    }

    /* A launched daemon that never opens its pipe yields a timeout failure */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task EnsureRunningAsync_DaemonNeverListens_ReturnsTimeoutFailure()
    {
        using var pipe = new DaemonPipeScope();
        var client = new DaemonClient(_ => null);

        var result = await client.EnsureRunningAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    /* StreamSpeechAsync returns the daemon's audio format and its PCM chunks */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_DaemonRunning_ReturnsFormatAndChunks()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new AudioFormat(24000, 1, 16), result.Value.Format);
        var chunks = await CollectAsync(result.Value.Chunks);
        Assert.Equal([Enumerable.Repeat((byte)0, 100), Enumerable.Repeat((byte)1, 100)], chunks);
    }

    /* StreamSpeechAsync sends the text and speed to the daemon */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_TextAndSpeed_ReachEngine()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello there", 0.9f, Ct);
        await CollectAsync(result.Value.Chunks);

        Assert.Equal(("hello there", (float?)0.9f), Assert.Single(daemon.Engine.Requests));
    }

    /* StreamSpeechAsync fails when no daemon listens */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_NoDaemon_ReturnsFailure()
    {
        using var pipe = new DaemonPipeScope();

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("Failed to stream speech from daemon", result.ErrorMessage);
    }

    /* A failed daemon response yields a failure with the daemon's message */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_DaemonReportsFailure_ReturnsDaemonMessage()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        daemon.Repository.AppConfig.ActiveModel = default;
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("not loaded", result.ErrorMessage);
    }

    /* A failed daemon response without message yields a generic failure */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_FailureWithoutMessage_ReturnsGenericFailure()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon((stream, _, ct) => WriteResponseAsync(stream, new DaemonResponse { Success = false }, ct));

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.Equal("Daemon synthesis failed.", result.ErrorMessage);
    }

    /* A "null" response yields a generic failure */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_NullResponse_ReturnsGenericFailure()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon((stream, _, ct) =>
            DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.Response, "null"u8.ToArray(), ct).AsTask());

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.Equal("Daemon synthesis failed.", result.ErrorMessage);
    }

    /* A first frame that is not a Response yields a failure naming the opcode */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_FirstFrameNotResponse_ReturnsUnexpectedOpcodeFailure()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon((stream, _, ct) =>
            DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioChunk, new byte[4], ct).AsTask());

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("Unexpected opcode from daemon: AudioChunk", result.ErrorMessage);
    }

    /* A successful response without audio format yields a failure */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_ResponseWithoutFormat_ReturnsFailure()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon((stream, _, ct) => WriteResponseAsync(stream, new DaemonResponse { Success = true }, ct));

        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("AudioFormat", result.ErrorMessage);
    }

    /* An Error frame mid-stream surfaces as an exception carrying the daemon's message */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_ErrorFrameMidStream_ThrowsWithDaemonMessage()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        daemon.Engine.ThrowOnSynthesize = new InvalidOperationException("engine exploded");
        await daemon.StartAsync(pipe, Ct);
        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(result.Value.Chunks));

        Assert.Contains("engine exploded", ex.Message);
    }

    /* A frame of an unknown kind mid-stream ends the stream */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_UnknownFrameMidStream_EndsStream()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon(async (stream, _, ct) =>
        {
            await WriteResponseAsync(stream, new DaemonResponse { Success = true, Format = ScriptedFormat }, ct);
            await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioChunk, new byte[] { 7, 7 }, ct);
            await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.Request, ReadOnlyMemory<byte>.Empty, ct);
            await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioChunk, new byte[] { 8, 8 }, ct);
        });
        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.Equal(ScriptedFormat, result.Value.Format);
        Assert.Equal([new byte[] { 7, 7 }], await CollectAsync(result.Value.Chunks));
    }

    /* A connection closed mid-stream ends the stream with the chunks received so far */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_ConnectionClosedMidStream_EndsStream()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon(async (stream, _, ct) =>
        {
            await WriteResponseAsync(stream, new DaemonResponse { Success = true, Format = ScriptedFormat }, ct);
            await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioChunk, new byte[] { 7, 7 }, ct);
        });
        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: Ct);

        Assert.Equal([new byte[] { 7, 7 }], await CollectAsync(result.Value.Chunks));
    }

    /* A cancelled token ends the chunk stream without an exception */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_CancelledBeforeReading_EndsStreamQuietly()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: cts.Token);

        cts.Cancel();

        Assert.Empty(await CollectAsync(result.Value.Chunks));
    }

    /* A cancelled wait for the next frame ends the chunk stream without an exception */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StreamSpeechAsync_CancelledWhileWaitingForFrame_EndsStreamQuietly()
    {
        using var pipe = new DaemonPipeScope();
        var release = new TaskCompletionSource();
        await using var daemon = new ScriptedDaemon(async (stream, _, ct) =>
        {
            await WriteResponseAsync(stream, new DaemonResponse { Success = true, Format = ScriptedFormat }, ct);
            await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioChunk, new byte[] { 1 }, ct);
            await release.Task.WaitAsync(ct);
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var result = await ClientThatMustNotLaunch().StreamSpeechAsync("hello", ct: cts.Token);
        var received = new List<byte[]>();

        await foreach (var chunk in result.Value.Chunks)
        {
            received.Add(chunk.ToArray());
            cts.Cancel();
        }

        release.TrySetResult();
        Assert.Single(received);
    }

    /* SynthesizeAsync wraps the streamed PCM in a WAV header of the daemon's format */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task SynthesizeAsync_DaemonRunning_ReturnsWav()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().SynthesizeAsync("hello", ct: Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        byte[] wav = result.Value!;
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(24000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(200, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(244, wav.Length);
    }

    /* SynthesizeAsync forwards a failed stream start */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task SynthesizeAsync_DaemonReportsFailure_ReturnsFailure()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        daemon.Repository.AppConfig.ActiveModel = default;
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().SynthesizeAsync("hello", ct: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("not loaded", result.ErrorMessage);
    }

    /* SynthesizeAsync turns a mid-stream error into a failure */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task SynthesizeAsync_ErrorMidStream_ReturnsFailure()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        daemon.Engine.ThrowOnSynthesize = new InvalidOperationException("engine exploded");
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().SynthesizeAsync("hello", ct: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("engine exploded", result.ErrorMessage);
    }

    /* KillDaemonAsync fails when no daemon runs */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task KillDaemonAsync_NoDaemon_ReturnsNotRunningFailure()
    {
        using var pipe = new DaemonPipeScope();

        var result = await ClientThatMustNotLaunch().KillDaemonAsync(Ct);

        Assert.Equal("Daemon is not running.", result.ErrorMessage);
    }

    /* KillDaemonAsync stops a running daemon */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task KillDaemonAsync_DaemonRunning_StopsDaemon()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var result = await ClientThatMustNotLaunch().KillDaemonAsync(Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        await daemon.RunTask!.WaitAsync(Generous, Ct);
    }

    /* KillDaemonAsync fails when the daemon refuses to stop */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task KillDaemonAsync_DaemonRefuses_ReturnsFailure()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon((stream, _, ct) => WriteResponseAsync(stream, new DaemonResponse { Success = false }, ct));

        var result = await ClientThatMustNotLaunch().KillDaemonAsync(Ct);

        Assert.Equal("Failure to stop daemon.", result.ErrorMessage);
    }

    /* GetStatusAsync returns null when no daemon runs */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task GetStatusAsync_NoDaemon_ReturnsNull()
    {
        using var pipe = new DaemonPipeScope();

        Assert.Null(await ClientThatMustNotLaunch().GetStatusAsync(Ct));
    }

    /* GetStatusAsync returns the daemon's status */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task GetStatusAsync_DaemonRunning_ReturnsStatus()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var status = await ClientThatMustNotLaunch().GetStatusAsync(Ct);

        Assert.NotNull(status);
        Assert.True(status.Success);
        Assert.Equal(InProcessDaemon.Model.ToString(), status.ActiveModel);
    }

    /* GetStatusAsync sends a status request */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task GetStatusAsync_DaemonRunning_SendsStatusAction()
    {
        using var pipe = new DaemonPipeScope();
        var actions = new List<string>();
        await using var daemon = new ScriptedDaemon((stream, request, ct) =>
        {
            actions.Add(request.Action);
            return WriteResponseAsync(stream, new DaemonResponse { Success = true }, ct);
        });

        await ClientThatMustNotLaunch().GetStatusAsync(Ct);

        Assert.Equal([DaemonIpcProtocol.ActionStatus], actions);
    }

    /* A status reply that is not a Response frame yields null */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task GetStatusAsync_ReplyNotResponse_ReturnsNull()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon((stream, _, ct) =>
            DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioEnd, ReadOnlyMemory<byte>.Empty, ct).AsTask());

        Assert.Null(await ClientThatMustNotLaunch().GetStatusAsync(Ct));
    }

    /* A status reply with malformed JSON yields null */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task GetStatusAsync_MalformedReply_ReturnsNull()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new ScriptedDaemon((stream, _, ct) =>
            DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.Response, Encoding.UTF8.GetBytes("{ broken"), ct).AsTask());

        Assert.Null(await ClientThatMustNotLaunch().GetStatusAsync(Ct));
    }

    /* Dummy daemon answering each request with a script. The next server instance is created before the current
       client is handled, so the listening socket stays open between clients (IsRunningAsync probes included) */
    private sealed class ScriptedDaemon : IAsyncDisposable
    {
        private readonly Func<Stream, DaemonRequest, CancellationToken, Task> _respond;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        /* Starts accepting clients right away (the pipe exists once the constructor returns) */
        public ScriptedDaemon(Func<Stream, DaemonRequest, CancellationToken, Task> respond)
        {
            _respond = respond;
            var first = CreateServer();
            _loop = Task.Run(() => AcceptLoopAsync(first));
        }

        /* One server instance of the daemon pipe */
        private static NamedPipeServerStream CreateServer() =>
            new(DaemonIpcProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        /* Accepts clients one at a time until stopped */
        private async Task AcceptLoopAsync(NamedPipeServerStream next)
        {
            while (true)
            {
                var current = next;
                try
                {
                    await current.WaitForConnectionAsync(_stop.Token);
                }
                catch (OperationCanceledException)
                {
                    await current.DisposeAsync();
                    return;
                }

                next = CreateServer();
                await using (current)
                {
                    try
                    {
                        var (_, payload) = await DaemonIpcProtocol.ReadFrameAsync(current, _stop.Token);
                        var request = JsonSerializer.Deserialize(payload, AppJsonContext.Default.DaemonRequest)!;
                        await _respond(current, request, _stop.Token);
                    }
                    catch (Exception ex) when (ex is EndOfStreamException or IOException or OperationCanceledException or JsonException)
                    {
                        /* Probe connections close without sending a request */
                    }
                }
            }
        }

        /* Stops accepting and waits for the loop to finish */
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _loop.WaitAsync(Generous);
            _stop.Dispose();
        }
    }
}
