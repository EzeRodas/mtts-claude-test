using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Daemon;
using MoonTTS.Infrastructure.Daemon.DTOs;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.Infrastructure.Tests.Daemon;

/* DaemonServer: synthesis framing (driven directly over a memory stream) and request routing, control actions
   and resilience (driven over a real, isolated named pipe with an in-process server) */
[Collection(TestCollections.Environment)]
public sealed class DaemonServerTests
{
    private const string PipesArePosixOnly = "Named pipes are machine wide on Windows and cannot be isolated per test";
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* Runs HandleSynthesizeAsync into a memory stream and returns every frame it wrote */
    private static async Task<List<(DaemonOpcode Opcode, byte[] Payload)>> SynthesizeToFramesAsync(InProcessDaemon daemon, DaemonRequest request)
    {
        using var stream = new MemoryStream();
        await daemon.Server.HandleSynthesizeAsync(stream, request, Ct);
        return await ReadAllFramesAsync(stream);
    }

    /* Decodes every frame of a written stream */
    private static async Task<List<(DaemonOpcode Opcode, byte[] Payload)>> ReadAllFramesAsync(MemoryStream stream)
    {
        stream.Position = 0;
        var frames = new List<(DaemonOpcode, byte[])>();
        while (stream.Position < stream.Length)
            frames.Add(await DaemonIpcProtocol.ReadFrameAsync(stream, Ct));
        return frames;
    }

    /* Parses a Response frame */
    private static DaemonResponse ParseResponse(byte[] payload) =>
        JsonSerializer.Deserialize(payload, AppJsonContext.Default.DaemonResponse)!;

    /* Connects a raw client to the daemon pipe */
    private static async Task<NamedPipeClientStream> ConnectAsync()
    {
        var client = new NamedPipeClientStream(".", DaemonIpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync((int)Generous.TotalMilliseconds, Ct);
        return client;
    }

    /* Sends one raw frame and returns the first reply frame, or null when the daemon closed the connection */
    private static async Task<(DaemonOpcode Opcode, byte[] Payload)?> SendRawAsync(DaemonOpcode opcode, byte[] payload)
    {
        await using var client = await ConnectAsync();
        await DaemonIpcProtocol.WriteFrameAsync(client, opcode, payload, Ct);
        try
        {
            return await DaemonIpcProtocol.ReadFrameAsync(client, Ct).AsTask().WaitAsync(Generous, Ct);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    /* Sends a control request and parses the reply */
    private static async Task<DaemonResponse?> SendRequestAsync(DaemonRequest request)
    {
        var reply = await SendRawAsync(DaemonOpcode.Request, JsonSerializer.SerializeToUtf8Bytes(request, AppJsonContext.Default.DaemonRequest));
        return reply is { Opcode: DaemonOpcode.Response } frame ? ParseResponse(frame.Payload) : null;
    }

    /* The first frame announces success, the engine's audio format and the active model */
    [Fact]
    public async Task HandleSynthesizeAsync_AvailableEngine_StartsWithFormatResponse()
    {
        await using var daemon = new InProcessDaemon();

        var frames = await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello" });

        Assert.Equal(DaemonOpcode.Response, frames[0].Opcode);
        var response = ParseResponse(frames[0].Payload);
        Assert.True(response.Success);
        Assert.Equal(new AudioFormat(24000, 1, 16), response.Format);
        Assert.Equal(InProcessDaemon.Model.ToString(), response.ActiveModel);
    }

    /* Every PCM chunk is sent as an AudioChunk frame, followed by AudioEnd */
    [Fact]
    public async Task HandleSynthesizeAsync_AvailableEngine_StreamsChunksThenAudioEnd()
    {
        await using var daemon = new InProcessDaemon();

        var frames = await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello" });

        Assert.Equal([DaemonOpcode.Response, DaemonOpcode.AudioChunk, DaemonOpcode.AudioChunk, DaemonOpcode.AudioEnd],
            frames.Select(f => f.Opcode));
        Assert.Equal(Enumerable.Repeat((byte)0, 100), frames[1].Payload);
        Assert.Equal(Enumerable.Repeat((byte)1, 100), frames[2].Payload);
        Assert.Empty(frames[3].Payload);
    }

    /* The requested speed is passed to the engine */
    [Fact]
    public async Task HandleSynthesizeAsync_Speed_IsPassedToEngine()
    {
        await using var daemon = new InProcessDaemon();

        await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello", Speed = 1.75f });

        Assert.Equal(("hello", (float?)1.75f), Assert.Single(daemon.Engine.Requests));
    }

    /* Dictionary replacements are applied before synthesis */
    [Fact]
    public async Task HandleSynthesizeAsync_DictionaryEntry_IsAppliedBeforeSynthesis()
    {
        await using var daemon = new InProcessDaemon();
        daemon.Repository.SaveDictionary([new DictionaryEntry { Original = "brb", Spelling = "be right back" }]);

        await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "brb now" });

        Assert.Equal("be right back now", Assert.Single(daemon.Engine.Requests).Text);
    }

    /* Text longer than the splitting threshold is synthesized sentence by sentence */
    [Fact]
    public async Task HandleSynthesizeAsync_LongText_SynthesizesEachSentence()
    {
        await using var daemon = new InProcessDaemon();
        string text = "This first sentence is long enough on its own. This second one makes the text exceed eighty characters!";

        var frames = await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = text });

        Assert.Equal(["This first sentence is long enough on its own.", "This second one makes the text exceed eighty characters!"],
            daemon.Engine.Requests.Select(r => r.Text));
        Assert.Equal(4, frames.Count(f => f.Opcode == DaemonOpcode.AudioChunk));
    }

    /* Short text is synthesized in one piece even when it has several sentences */
    [Fact]
    public async Task HandleSynthesizeAsync_ShortText_IsNotSplit()
    {
        await using var daemon = new InProcessDaemon();

        await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "Hi. Bye." });

        Assert.Equal("Hi. Bye.", Assert.Single(daemon.Engine.Requests).Text);
    }

    /* Settings are reloaded from disk for every synthesis */
    [Fact]
    public async Task HandleSynthesizeAsync_Request_ReloadsSettings()
    {
        await using var daemon = new InProcessDaemon();

        await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello" });

        Assert.Equal(1, daemon.Repository.LoadAllCount);
    }

    /* A model changed in settings since the last request is loaded before synthesis */
    [Fact]
    public async Task HandleSynthesizeAsync_ModelChangedInSettings_SwitchesEngine()
    {
        await using var daemon = new InProcessDaemon();
        await daemon.Provider.GetEngineAsync(Ct);
        var otherModel = new ModelId("fake", "other");
        var otherEngine = new FakeTtsEngine();
        daemon.Factory.Engines[otherModel] = () => otherEngine;
        daemon.Repository.AppConfig.ActiveModel = otherModel;

        var frames = await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello" });

        Assert.Equal(otherModel, daemon.Provider.CurrentModel);
        Assert.Single(otherEngine.Requests);
        Assert.Empty(daemon.Engine.Requests);
        Assert.Equal(otherModel.ToString(), ParseResponse(frames[0].Payload).ActiveModel);
    }

    /* An unavailable engine yields a single failed Response frame */
    [Fact]
    public async Task HandleSynthesizeAsync_EngineUnavailable_SendsFailureResponseOnly()
    {
        await using var daemon = new InProcessDaemon();
        daemon.Repository.AppConfig.ActiveModel = ModelId.Unselected;

        var frames = await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello" });

        var frame = Assert.Single(frames);
        Assert.Equal(DaemonOpcode.Response, frame.Opcode);
        var response = ParseResponse(frame.Payload);
        Assert.False(response.Success);
        Assert.Contains("not loaded", response.ErrorMessage);
    }

    /* An engine failure is reported as an Error frame carrying the message */
    [Fact]
    public async Task HandleSynthesizeAsync_EngineThrows_SendsErrorFrame()
    {
        await using var daemon = new InProcessDaemon();
        daemon.Engine.ThrowOnSynthesize = new InvalidOperationException("engine exploded");

        var frames = await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello" });

        Assert.Equal([DaemonOpcode.Response, DaemonOpcode.Error], frames.Select(f => f.Opcode));
        Assert.Equal("engine exploded", Encoding.UTF8.GetString(frames[1].Payload));
    }

    /* A cancelled synthesis ends quietly, without AudioEnd or Error */
    [Fact]
    public async Task HandleSynthesizeAsync_Cancelled_EndsWithoutAudioEndOrError()
    {
        await using var daemon = new InProcessDaemon();
        daemon.Engine.ThrowOnSynthesize = new OperationCanceledException();

        var frames = await SynthesizeToFramesAsync(daemon, new DaemonRequest { Text = "hello" });

        Assert.Equal([DaemonOpcode.Response], frames.Select(f => f.Opcode));
    }

    /* RunAsync loads the selected model before accepting clients */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_Start_PrewarmsSelectedModel()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();

        await daemon.StartAsync(pipe, Ct);

        Assert.Equal([InProcessDaemon.Model], daemon.Factory.CreatedModels);
    }

    /* RunAsync returns when its token is cancelled */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_Cancelled_Returns()
    {
        using var pipe = new DaemonPipeScope();
        var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        await daemon.DisposeAsync();

        Assert.True(daemon.RunTask!.IsCompletedSuccessfully);
    }

    /* A status request reports success, the selected model and the idle time left */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_StatusRequest_ReportsModelAndIdleTime()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var response = await SendRequestAsync(new DaemonRequest { Action = DaemonIpcProtocol.ActionStatus });

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(InProcessDaemon.Model.ToString(), response.ActiveModel);
        Assert.InRange(response.IdleSecondsRemaining, 1, 300);
    }

    /* A kill request is acknowledged */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_KillRequest_RepliesSuccess()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var response = await SendRequestAsync(new DaemonRequest { Action = DaemonIpcProtocol.ActionKill });

        Assert.True(response?.Success);
    }

    /* A kill request makes RunAsync return */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_KillRequest_StopsServer()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        await SendRequestAsync(new DaemonRequest { Action = DaemonIpcProtocol.ActionKill });

        await daemon.RunTask!.WaitAsync(Generous, Ct);
        Assert.True(daemon.RunTask.IsCompletedSuccessfully);
    }

    /* A synthesize request over the pipe streams the engine's audio */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_SynthesizeRequest_StreamsAudio()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);
        await using var client = await ConnectAsync();
        var request = new DaemonRequest { Action = DaemonIpcProtocol.ActionSynthesize, Text = "hello" };
        await DaemonIpcProtocol.WriteFrameAsync(client, DaemonOpcode.Request, JsonSerializer.SerializeToUtf8Bytes(request, AppJsonContext.Default.DaemonRequest), Ct);

        var opcodes = new List<DaemonOpcode>();
        DaemonOpcode opcode;
        do
        {
            (opcode, _) = await DaemonIpcProtocol.ReadFrameAsync(client, Ct).AsTask().WaitAsync(Generous, Ct);
            opcodes.Add(opcode);
        } while (opcode is DaemonOpcode.Response or DaemonOpcode.AudioChunk);

        Assert.Equal([DaemonOpcode.Response, DaemonOpcode.AudioChunk, DaemonOpcode.AudioChunk, DaemonOpcode.AudioEnd], opcodes);
    }

    /* An unknown action is treated as a synthesize request */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_UnknownAction_IsHandledAsSynthesis()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        var response = await SendRequestAsync(new DaemonRequest { Action = "something-else", Text = "hello" });

        Assert.NotNull(response?.Format);
        Assert.Equal("hello", Assert.Single(daemon.Engine.Requests).Text);
    }

    /* A first frame that is not a Request is ignored and the connection closed without reply */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_NonRequestFrame_ClosesWithoutReply()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        Assert.Null(await SendRawAsync(DaemonOpcode.Cancel, []));
    }

    /* A request whose JSON is malformed is dropped without reply */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_MalformedRequest_ClosesWithoutReply()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        Assert.Null(await SendRawAsync(DaemonOpcode.Request, "{ not json"u8.ToArray()));
    }

    /* A null request is dropped without reply */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_NullRequest_ClosesWithoutReply()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);

        Assert.Null(await SendRawAsync(DaemonOpcode.Request, "null"u8.ToArray()));
    }

    /* After a malformed request the daemon keeps serving the next client */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_AfterMalformedRequest_KeepsServing()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);
        await SendRawAsync(DaemonOpcode.Request, "{ not json"u8.ToArray());

        var response = await SendRequestAsync(new DaemonRequest { Action = DaemonIpcProtocol.ActionStatus });

        Assert.True(response?.Success);
    }

    /* A request sent right after a connectivity probe is served (the CLI always probes first): the next pipe
       instance must already listen while the probe connection is being finished */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_RequestRightAfterProbe_IsServedEveryTime()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);
        var client = new DaemonClient(_ => throw new InvalidOperationException("must not launch"));

        int served = 0;
        for (int i = 0; i < 25; i++)
        {
            if (await client.IsRunningAsync() && await client.GetStatusAsync(Ct) is { Success: true })
                served++;
        }

        Assert.Equal(25, served);
    }

    /* While the pipe cannot be created the server waits between attempts instead of spinning on the CPU */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_PipeCannotBeCreated_DoesNotSpin()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        Directory.CreateDirectory(pipe.SocketPath);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            var cpuBefore = self.TotalProcessorTime;
            var run = Task.Run(() => daemon.Server.RunAsync(stop.Token), Ct);

            await Task.Delay(TimeSpan.FromSeconds(2), Ct);
            self.Refresh();
            var cpuUsed = self.TotalProcessorTime - cpuBefore;
            stop.Cancel();
            await run.WaitAsync(Generous, Ct);

            Assert.True(cpuUsed < TimeSpan.FromSeconds(1), $"RunAsync used {cpuUsed.TotalMilliseconds:0} ms of CPU in 2 s");
        }
        finally
        {
            Directory.Delete(pipe.SocketPath);
        }
    }

    /* Once the pipe can be created again the server starts listening on its own */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_PipeBecomesAvailable_StartsListening()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        Directory.CreateDirectory(pipe.SocketPath);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Task.Run(() => daemon.Server.RunAsync(stop.Token), Ct);

        Directory.Delete(pipe.SocketPath);
        bool listening = await InfraWait.UntilAsync(() => File.Exists(pipe.SocketPath), Generous, Ct);
        var status = await SendRequestAsync(new DaemonRequest { Action = DaemonIpcProtocol.ActionStatus });
        stop.Cancel();
        await run.WaitAsync(Generous, Ct);

        Assert.True(listening);
        Assert.True(status?.Success);
    }

    /* Cancelling while the server waits to retry the pipe makes RunAsync return */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_CancelledWhilePipeUnavailable_Returns()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        Directory.CreateDirectory(pipe.SocketPath);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        try
        {
            var run = Task.Run(() => daemon.Server.RunAsync(stop.Token), Ct);

            stop.CancelAfter(TimeSpan.FromMilliseconds(300));

            await run.WaitAsync(Generous, Ct);
            Assert.True(run.IsCompletedSuccessfully);
        }
        finally
        {
            Directory.Delete(pipe.SocketPath);
        }
    }

    /* A client that disconnects without sending anything does not stop the daemon */
    [Fact(Skip = PipesArePosixOnly, SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task RunAsync_ClientDisconnectsSilently_KeepsServing()
    {
        using var pipe = new DaemonPipeScope();
        await using var daemon = new InProcessDaemon();
        await daemon.StartAsync(pipe, Ct);
        await (await ConnectAsync()).DisposeAsync();

        var response = await SendRequestAsync(new DaemonRequest { Action = DaemonIpcProtocol.ActionStatus });

        Assert.True(response?.Success);
    }
}
