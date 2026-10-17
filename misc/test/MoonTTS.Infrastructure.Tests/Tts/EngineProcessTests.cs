using MoonTTS.Domain.ValueObjects;
using MoonTTS.Engine.Protocol;
using MoonTTS.Infrastructure.Tts;
using MoonTTS.Tests.DummyEngine;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* Drives EngineProcess against the real dummy worker process: request/reply exchanges, streaming,
   cancellation with drain, protocol violations, timeouts and shutdown */
public sealed class EngineProcessTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* Sends Ping and returns the reply opcode, proving the channel is still in sync */
    private static async Task<EngineOpcode> PingAsync(EngineProcess process) =>
        (await process.RequestAsync(EngineOpcode.Ping, ReadOnlyMemory<byte>.Empty, EngineOpcode.Pong, Generous, Ct)).Opcode;

    /* Streams one Synthesize request and collects every chunk */
    private static async Task<List<byte[]>> StreamAllAsync(EngineProcess process, string text)
    {
        var chunks = new List<byte[]>();
        await foreach (var chunk in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload(text), Ct))
            chunks.Add(chunk.ToArray());
        return chunks;
    }

    /* JSON payload of a Synthesize request */
    private static byte[] SynthesizePayload(string text) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new SynthesizeRequest { Text = text, VoiceId = "v1" }, EngineProtocolJsonContext.Default.SynthesizeRequest);

    /* Start refuses an engine whose worker executable does not exist */
    [Fact]
    public void Start_MissingExecutable_ThrowsEngineWorkerException()
    {
        using var temp = new TempDirectory();
        var engine = new EngineDescriptor("ghost", "Ghost", temp.Path, temp.Combine("missing-worker"), []);

        var ex = Assert.Throws<EngineWorkerException>(() => EngineProcess.Start(engine));

        Assert.Contains("missing-worker", ex.Message);
    }

    /* Start runs the worker inside its engine folder (the dummy reads dummy-engine.json from its working directory) */
    [Fact]
    public async Task Start_ValidEngine_RunsWorkerInsideEngineFolder()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { EngineId = "cwd-check" });
        using var process = harness.StartProcess();

        var metadata = await process.RequestJsonAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty,
            EngineOpcode.MetadataResponse, EngineProtocolJsonContext.Default.EngineMetadataMessage, Generous, Ct);

        Assert.Equal("cwd-check", metadata.EngineId);
    }

    /* HasExited is false while the worker is alive */
    [Fact]
    public async Task HasExited_LiveWorker_ReturnsFalse()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();
        await PingAsync(process);

        Assert.False(process.HasExited);
    }

    /* HasExited reports true once the process object is gone */
    [Fact]
    public void HasExited_DisposedProcess_ReturnsTrue()
    {
        using var harness = new DummyEngineHarness();
        var process = harness.StartProcess();
        process.Dispose();

        Assert.True(process.HasExited);
    }

    /* HasExited is true as soon as the worker closed its stdout, before the OS reports the process exit */
    [Fact]
    public async Task HasExited_WorkerClosedStdout_ReturnsTrueImmediately()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();
        await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.Shutdown, ReadOnlyMemory<byte>.Empty, EngineOpcode.Pong, Generous, Ct));

        Assert.True(process.HasExited);
    }

    /* HasExited is true as soon as the worker's stdout went out of sync */
    [Fact]
    public async Task HasExited_ChannelOutOfSync_ReturnsTrueImmediately()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { GarbageOnMetadata = true });
        using var process = harness.StartProcess();
        await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty, EngineOpcode.MetadataResponse, Generous, Ct));

        Assert.True(process.HasExited);
    }

    /* RequestAsync returns the reply frame carrying the expected opcode */
    [Fact]
    public async Task RequestAsync_ExpectedReply_ReturnsFrame()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        var frame = await process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty,
            EngineOpcode.MetadataResponse, Generous, Ct);

        Assert.Equal(EngineOpcode.MetadataResponse, frame.Opcode);
        Assert.False(frame.Payload.IsEmpty);
    }

    /* RequestAsync sends the request opcode and payload to the worker */
    [Fact]
    public async Task RequestAsync_Request_IsReceivedByWorker()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        await process.RequestAsync(EngineOpcode.LoadModel, "{\"modelId\":\"m\"}"u8.ToArray(), EngineOpcode.ModelLoaded, Generous, Ct);

        var request = Assert.Single(harness.ReadRequests());
        Assert.Equal(EngineOpcode.LoadModel, request.Opcode);
        Assert.Equal("m", request.Json.GetProperty("modelId").GetString());
    }

    /* RequestJsonAsync deserializes the reply payload */
    [Fact]
    public async Task RequestJsonAsync_MetadataReply_DeserializesPayload()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { SampleRate = 16000, Channels = 2, BitDepth = 24 });
        using var process = harness.StartProcess();

        var metadata = await process.RequestJsonAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty,
            EngineOpcode.MetadataResponse, EngineProtocolJsonContext.Default.EngineMetadataMessage, Generous, Ct);

        Assert.Equal("dummy", metadata.EngineId);
        Assert.Equal(16000, metadata.AudioFormat.SampleRate);
        Assert.Equal(2, metadata.AudioFormat.Channels);
        Assert.Equal(24, metadata.AudioFormat.BitDepth);
    }

    /* An Error reply raises EngineWorkerException carrying the worker's code and message */
    [Fact]
    public async Task RequestAsync_ErrorReply_ThrowsWithWorkerCodeAndMessage()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { FailLoadModel = true });
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.LoadModel, "{}"u8.ToArray(), EngineOpcode.ModelLoaded, Generous, Ct));

        Assert.Equal("model_load_failed", ex.Code);
        Assert.Contains("dummy model load failure", ex.Message);
    }

    /* An Error reply is a complete exchange, so the worker keeps answering afterwards */
    [Fact]
    public async Task RequestAsync_AfterErrorReply_WorkerStillAnswers()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { FailLoadModel = true });
        using var process = harness.StartProcess();
        await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.LoadModel, "{}"u8.ToArray(), EngineOpcode.ModelLoaded, Generous, Ct));

        Assert.Equal(EngineOpcode.Pong, await PingAsync(process));
    }

    /* A reply with an unexpected opcode raises EngineWorkerException naming both opcodes */
    [Fact]
    public async Task RequestAsync_UnexpectedReplyOpcode_ThrowsEngineWorkerException()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.Ping, ReadOnlyMemory<byte>.Empty, EngineOpcode.MetadataResponse, Generous, Ct));

        Assert.Contains("Pong", ex.Message);
        Assert.Contains("MetadataResponse", ex.Message);
    }

    /* A worker that never answers makes RequestAsync fail with a timeout error */
    [Fact]
    public async Task RequestAsync_WorkerDoesNotAnswer_ThrowsTimeoutError()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { HangOnMetadata = true });
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty,
                EngineOpcode.MetadataResponse, TimeSpan.FromMilliseconds(500), Ct));

        Assert.Contains("did not answer GetMetadata", ex.Message);
    }

    /* A worker that never answers is killed, since its channel state is unknown */
    [Fact]
    public async Task RequestAsync_WorkerDoesNotAnswer_KillsWorker()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { HangOnMetadata = true });
        using var process = harness.StartProcess();

        await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty,
                EngineOpcode.MetadataResponse, TimeSpan.FromMilliseconds(500), Ct));

        Assert.True(await InfraWait.UntilAsync(() => process.HasExited, ExitWait, Ct));
    }

    /* Cancelling while waiting for a reply propagates the cancellation and kills the half-read worker */
    [Fact]
    public async Task RequestAsync_CallerCancelsWhileWaiting_ThrowsCanceledAndKillsWorker()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { HangOnMetadata = true });
        using var process = harness.StartProcess();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty,
                EngineOpcode.MetadataResponse, Generous, cts.Token));

        Assert.True(await InfraWait.UntilAsync(() => process.HasExited, ExitWait, Ct));
    }

    /* An already cancelled token stops RequestAsync before anything is sent */
    [Fact]
    public async Task RequestAsync_AlreadyCancelledToken_SendsNothing()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty,
                EngineOpcode.MetadataResponse, Generous, new CancellationToken(canceled: true)));
        await PingAsync(process);

        Assert.Equal([EngineOpcode.Ping], harness.ReadRequests().Select(r => r.Opcode));
    }

    /* A worker that exits instead of replying raises an "exited" EngineWorkerException */
    [Fact]
    public async Task RequestAsync_WorkerExitsBeforeReplying_ThrowsExitedError()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.Shutdown, ReadOnlyMemory<byte>.Empty, EngineOpcode.Pong, Generous, Ct));

        Assert.Contains("exited during Shutdown", ex.Message);
    }

    /* Bytes that are not a protocol frame raise a "lost the channel" EngineWorkerException */
    [Fact]
    public async Task RequestAsync_GarbageOnStdout_ThrowsLostChannelError()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { GarbageOnMetadata = true });
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty, EngineOpcode.MetadataResponse, Generous, Ct));

        Assert.Contains("Lost the channel", ex.Message);
        Assert.IsType<InvalidDataException>(ex.InnerException);
    }

    /* A worker whose stdout went out of sync is killed */
    [Fact]
    public async Task RequestAsync_GarbageOnStdout_KillsWorker()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { GarbageOnMetadata = true });
        using var process = harness.StartProcess();

        await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.GetMetadata, ReadOnlyMemory<byte>.Empty, EngineOpcode.MetadataResponse, Generous, Ct));

        Assert.True(await InfraWait.UntilAsync(() => process.HasExited, ExitWait, Ct));
    }

    /* Writing to a worker that already exited raises a "not accepting requests" EngineWorkerException */
    [Fact]
    public async Task RequestAsync_WorkerAlreadyExited_ThrowsNotAcceptingRequests()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();
        await Assert.ThrowsAsync<EngineWorkerException>(() =>
            process.RequestAsync(EngineOpcode.Shutdown, ReadOnlyMemory<byte>.Empty, EngineOpcode.Pong, Generous, Ct));
        Assert.True(await InfraWait.UntilAsync(() => process.HasExited, ExitWait, Ct));

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() => PingAsync(process));

        Assert.Contains("not accepting requests", ex.Message);
    }

    /* StreamAsync yields every AudioChunk payload in the order the worker sent them */
    [Fact]
    public async Task StreamAsync_CompleteStream_YieldsChunksInOrder()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 3, ChunkSize = 16 });
        using var process = harness.StartProcess();

        var chunks = await StreamAllAsync(process, "hello");

        Assert.Equal(3, chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(Enumerable.Repeat((byte)i, 16), chunks[i]);
    }

    /* StreamAsync sends the request opcode and payload */
    [Fact]
    public async Task StreamAsync_Request_IsReceivedByWorker()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        await StreamAllAsync(process, "hello");

        var request = Assert.Single(harness.ReadRequests());
        Assert.Equal(EngineOpcode.Synthesize, request.Opcode);
        Assert.Equal("hello", request.Json.GetProperty("text").GetString());
    }

    /* Empty AudioChunk frames are not yielded */
    [Fact]
    public async Task StreamAsync_EmptyChunks_AreSkipped()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 3, ChunkSize = 0 });
        using var process = harness.StartProcess();

        Assert.Empty(await StreamAllAsync(process, "hello"));
    }

    /* After a complete stream the channel is ready for the next exchange */
    [Fact]
    public async Task StreamAsync_CompleteStream_ChannelStaysInSync()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();
        await StreamAllAsync(process, "hello");

        Assert.Equal(EngineOpcode.Pong, await PingAsync(process));
    }

    /* An Error frame ends the stream with EngineWorkerException carrying the worker's code */
    [Fact]
    public async Task StreamAsync_ErrorFrame_ThrowsWithWorkerCode()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() => StreamAllAsync(process, harness.Options.ErrorOnText));

        Assert.Equal("synthesis_failed", ex.Code);
        Assert.Contains("dummy synthesis error", ex.Message);
    }

    /* An Error frame is terminal: no Cancel is sent and the worker keeps answering */
    [Fact]
    public async Task StreamAsync_ErrorFrame_WorkerStillAnswersWithoutCancel()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();
        await Assert.ThrowsAsync<EngineWorkerException>(() => StreamAllAsync(process, harness.Options.ErrorOnText));

        Assert.Equal(EngineOpcode.Pong, await PingAsync(process));
        Assert.DoesNotContain(harness.ReadRequests(), r => r.Opcode == EngineOpcode.Cancel);
    }

    /* An Error frame whose payload is not JSON still surfaces as an EngineWorkerException without code */
    [Fact]
    public async Task StreamAsync_UnreadableErrorFrame_ThrowsUnreadableError()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() => StreamAllAsync(process, harness.Options.UnreadableErrorOnText));

        Assert.Contains("unreadable error", ex.Message);
        Assert.Equal(string.Empty, ex.Code);
    }

    /* A frame that does not belong in a stream raises EngineWorkerException naming it */
    [Fact]
    public async Task StreamAsync_UnexpectedOpcode_ThrowsEngineWorkerException()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() => StreamAllAsync(process, harness.Options.UnexpectedOpcodeOnText));

        Assert.Contains("sent Pong while streaming Synthesize", ex.Message);
    }

    /* A worker that breaks the stream protocol is killed */
    [Fact]
    public async Task StreamAsync_UnexpectedOpcode_KillsWorker()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        await Assert.ThrowsAsync<EngineWorkerException>(() => StreamAllAsync(process, harness.Options.UnexpectedOpcodeOnText));

        Assert.True(await InfraWait.UntilAsync(() => process.HasExited, ExitWait, Ct));
    }

    /* A worker that exits before its first chunk raises an "exited" EngineWorkerException */
    [Fact]
    public async Task StreamAsync_WorkerCrashesBeforeAudio_ThrowsExitedError()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() => StreamAllAsync(process, harness.Options.CrashOnText));

        Assert.Contains("exited during Synthesize", ex.Message);
    }

    /* A worker that exits mid-stream yields the chunks it sent and then raises an "exited" error */
    [Fact]
    public async Task StreamAsync_WorkerExitsMidStream_YieldsSentChunksThenThrows()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 5, CrashAfterChunks = 2 });
        using var process = harness.StartProcess();
        var received = new List<byte[]>();

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(async () =>
        {
            await foreach (var chunk in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload("hello"), Ct))
                received.Add(chunk.ToArray());
        });

        Assert.Equal(2, received.Count);
        Assert.Contains("exited during Synthesize", ex.Message);
    }

    /* Cancelling a running stream sends a Cancel frame to the worker */
    [Fact]
    public async Task StreamAsync_Cancelled_SendsCancelFrame()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 200, ChunkDelayMs = 50 });
        using var process = harness.StartProcess();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload("hello"), cts.Token))
                cts.Cancel();
        });

        Assert.Contains(harness.ReadRequests(), r => r.Opcode == EngineOpcode.Cancel);
    }

    /* After a cancelled stream is drained the channel is ready for the next exchange */
    [Fact]
    public async Task StreamAsync_Cancelled_ChannelStaysInSync()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 200, ChunkDelayMs = 50 });
        using var process = harness.StartProcess();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload("hello"), cts.Token))
                cts.Cancel();
        });

        Assert.Equal(EngineOpcode.Pong, await PingAsync(process));
        Assert.False(process.HasExited);
    }

    /* Abandoning an enumeration early cancels and drains the stream so the channel stays in sync */
    [Fact]
    public async Task StreamAsync_AbandonedEnumeration_DrainsAndChannelStaysInSync()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 200, ChunkDelayMs = 20 });
        using var process = harness.StartProcess();

        await foreach (var _ in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload("hello"), Ct))
            break;

        Assert.Equal(EngineOpcode.Pong, await PingAsync(process));
        Assert.Contains(harness.ReadRequests(), r => r.Opcode == EngineOpcode.Cancel);
    }

    /* A worker that ignores Cancel is killed once the drain timeout expires */
    [Fact]
    public async Task StreamAsync_WorkerIgnoresCancel_KillsWorker()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 3, ChunkDelayMs = 50, IgnoreCancel = true });
        using var process = harness.StartProcess();

        await foreach (var _ in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload("hello"), Ct))
            break;

        Assert.True(await InfraWait.UntilAsync(() => process.HasExited, ExitWait, Ct));
    }

    /* An already cancelled token stops StreamAsync before the request is sent */
    [Fact]
    public async Task StreamAsync_AlreadyCancelledToken_SendsNothing()
    {
        using var harness = new DummyEngineHarness();
        using var process = harness.StartProcess();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload("hello"), new CancellationToken(canceled: true)))
            {
            }
        });
        await PingAsync(process);

        Assert.Equal([EngineOpcode.Ping], harness.ReadRequests().Select(r => r.Opcode));
    }

    /* Only one exchange runs at a time: a request issued mid-stream waits until the stream ends */
    [Fact]
    public async Task StreamAsync_RequestIssuedMidStream_WaitsForStreamToFinish()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 3, ChunkSize = 8 });
        using var process = harness.StartProcess();
        Task<EngineOpcode>? ping = null;
        int chunks = 0;

        await foreach (var _ in process.StreamAsync(EngineOpcode.Synthesize, SynthesizePayload("hello"), Ct))
        {
            chunks++;
            ping ??= PingAsync(process);
            await Task.Yield();
            Assert.False(ping.IsCompleted);
        }

        Assert.Equal(3, chunks);
        Assert.Equal(EngineOpcode.Pong, await ping!.WaitAsync(Generous, Ct));
    }

    /* Dispose closes the worker's stdin, which makes it exit on its own */
    [Fact]
    public async Task Dispose_LiveWorker_ClosesStdinSoWorkerExitsGracefully()
    {
        using var harness = new DummyEngineHarness();
        var process = harness.StartProcess();
        await PingAsync(process);

        process.Dispose();

        Assert.Contains("stdin-closed", harness.ReadExitLog());
    }

    /* Dispose kills a worker that keeps running after its stdin was closed */
    [Fact]
    public async Task Dispose_WorkerIgnoresStdinClose_KillsWorker()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { HangOnStdinClose = true });
        var process = harness.StartProcess();
        await PingAsync(process);
        int pid = harness.ReadWorkerPid();

        process.Dispose();

        Assert.True(await InfraWait.ProcessExitsAsync(pid, ExitWait, Ct));
    }

    /* Dispose can be called more than once */
    [Fact]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        using var harness = new DummyEngineHarness();
        var process = harness.StartProcess();
        await PingAsync(process);

        process.Dispose();
        var ex = Record.Exception(process.Dispose);

        Assert.Null(ex);
    }
}
