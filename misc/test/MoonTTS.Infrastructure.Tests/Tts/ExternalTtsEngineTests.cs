using System.Buffers.Binary;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Exceptions;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Engine.Protocol;
using MoonTTS.Infrastructure.Tts;
using MoonTTS.Tests.DummyEngine;

namespace MoonTTS.Infrastructure.Tests.Tts;

/* Drives ExternalTtsEngine against the real dummy worker: handshake validation, voices (voices.json vs ListVoices),
   per-model preferences, synthesis requests, WAV output, crash recovery and disposal */
public sealed class ExternalTtsEngineTests
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* Creates the engine and fails the test with the error message when it could not start */
    private static async Task<ITtsEngine> CreateOrFailAsync(DummyEngineHarness harness, string modelName = "default")
    {
        var result = await harness.CreateEngineAsync(modelName, Ct);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Value!;
    }

    /* Collects every chunk of one synthesis */
    private static async Task<List<byte[]>> SynthesizeAllAsync(ITtsEngine engine, string text, float? speed = null)
    {
        var chunks = new List<byte[]>();
        await foreach (var chunk in engine.SynthesizeStreamAsync(text, speed, Ct))
            chunks.Add(chunk.ToArray());
        return chunks;
    }

    /* Makes the worker crash mid-request. The next request is issued right away, before the OS may have
       reported the exit, which is exactly when the restart must still happen */
    private static Task CrashWorkerAsync(DummyEngineHarness harness, ITtsEngine engine) =>
        Assert.ThrowsAsync<EngineWorkerException>(() => SynthesizeAllAsync(engine, harness.Options.CrashOnText));

    /* The Synthesize requests the worker received */
    private static List<LoggedFrame> SynthesizeRequests(DummyEngineHarness harness) =>
        harness.ReadRequests().Where(r => r.Opcode == EngineOpcode.Synthesize).ToList();

    /* A worker that completes the handshake yields an available engine */
    [Fact]
    public async Task CreateAsync_ValidWorker_ReturnsAvailableEngine()
    {
        using var harness = new DummyEngineHarness();

        using var engine = await CreateOrFailAsync(harness);

        Assert.True(engine.IsAvailable);
    }

    /* The output format is the one the worker declared in its metadata */
    [Fact]
    public async Task CreateAsync_ValidWorker_UsesWorkerAudioFormat()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { SampleRate = 16000, Channels = 2, BitDepth = 24 });

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal(new AudioFormat(16000, 2, 24), engine.OutputFormat);
    }

    /* The handshake is GetMetadata, then LoadModel, then ListVoices when voices.json has no entry */
    [Fact]
    public async Task CreateAsync_NoVoicesFile_PerformsFullHandshakeInOrder()
    {
        using var harness = new DummyEngineHarness();

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal([EngineOpcode.GetMetadata, EngineOpcode.LoadModel, EngineOpcode.ListVoices],
            harness.ReadRequests().Select(r => r.Opcode));
    }

    /* LoadModel carries the model name, the engine's models folder and the model's download file names */
    [Fact]
    public async Task CreateAsync_ModelWithFiles_SendsLoadModelRequest()
    {
        using var harness = new DummyEngineHarness(null, new EngineModelManifest
        {
            Id = "voice-pack",
            DisplayName = "Voice Pack",
            DownloadFiles = new() { ["model.onnx"] = "https://example.invalid/model.onnx", ["voices.bin"] = "https://example.invalid/voices.bin" }
        });

        using var engine = await CreateOrFailAsync(harness, "voice-pack");

        var load = harness.ReadRequests().Single(r => r.Opcode == EngineOpcode.LoadModel).Json;
        Assert.Equal("voice-pack", load.GetProperty("modelId").GetString());
        Assert.Equal(Path.Combine(harness.ModelsDirectory, "dummy"), load.GetProperty("modelDirectory").GetString());
        Assert.Equal(["model.onnx", "voices.bin"], load.GetProperty("files").EnumerateArray().Select(f => f.GetString()).Order());
    }

    /* A worker reporting another engine id than its engine.json is rejected */
    [Fact]
    public async Task CreateAsync_EngineIdMismatch_ReturnsFailure()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ReportedEngineId = "impostor" });

        var result = await harness.CreateEngineAsync(cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("impostor", result.ErrorMessage);
        Assert.IsType<EngineWorkerException>(result.Exception);
    }

    /* A rejected handshake shuts the worker down */
    [Fact]
    public async Task CreateAsync_EngineIdMismatch_StopsWorker()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ReportedEngineId = "impostor" });

        await harness.CreateEngineAsync(cancellationToken: Ct);

        Assert.True(await InfraWait.ProcessExitsAsync(harness.ReadWorkerPid(), ExitWait, Ct));
    }

    /* Audio formats that cannot describe PCM are rejected */
    [Theory]
    [InlineData(0, 1, 16)]
    [InlineData(22050, 0, 16)]
    [InlineData(22050, 1, 0)]
    [InlineData(22050, 1, 12)]
    [InlineData(-1, 1, 16)]
    public async Task CreateAsync_InvalidAudioFormat_ReturnsFailure(int sampleRate, short channels, short bitDepth)
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { SampleRate = sampleRate, Channels = channels, BitDepth = bitDepth });

        var result = await harness.CreateEngineAsync(cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("invalid audio format", result.ErrorMessage);
    }

    /* A worker that fails LoadModel yields a failure carrying the worker's message */
    [Fact]
    public async Task CreateAsync_LoadModelFails_ReturnsFailureWithWorkerMessage()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { FailLoadModel = true });

        var result = await harness.CreateEngineAsync(cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("dummy model load failure", result.ErrorMessage);
        Assert.Equal("model_load_failed", Assert.IsType<EngineWorkerException>(result.Exception).Code);
    }

    /* A worker that breaks the protocol during the handshake yields a failure */
    [Fact]
    public async Task CreateAsync_GarbageMetadata_ReturnsFailure()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { GarbageOnMetadata = true });

        var result = await harness.CreateEngineAsync(cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.IsType<EngineWorkerException>(result.Exception);
    }

    /* A missing worker executable yields a failure instead of an exception */
    [Fact]
    public async Task CreateAsync_MissingExecutable_ReturnsFailure()
    {
        using var harness = new DummyEngineHarness();
        File.Delete(harness.Engine.ExecutablePath);

        var result = await harness.CreateEngineAsync(cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.ErrorMessage);
    }

    /* A worker file that cannot be executed yields a failure instead of an exception */
    [Fact(Skip = "Execute permissions are POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task CreateAsync_ExecutableWithoutExecutePermission_ReturnsFailure()
    {
        using var harness = new DummyEngineHarness();
        File.SetUnixFileMode(harness.Engine.ExecutablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var result = await harness.CreateEngineAsync(cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.IsType<System.ComponentModel.Win32Exception>(result.Exception);
    }

    /* Cancelling the handshake propagates the cancellation */
    [Fact]
    public async Task CreateAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { HangOnMetadata = true });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.CreateEngineAsync(cancellationToken: cts.Token));
    }

    /* Without a voices.json entry the voices come from the worker's ListVoices reply */
    [Fact]
    public async Task ListVoices_NoVoicesFile_ReturnsWorkerVoices()
    {
        using var harness = new DummyEngineHarness();

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal([new VoiceInfo("v1", "Voice One", "en-US"), new VoiceInfo("v2")], engine.ListVoices());
    }

    /* The ListVoices reply is written to voices.json for the next start */
    [Fact]
    public async Task CreateAsync_NoVoicesFile_WritesWorkerVoicesToVoicesFile()
    {
        using var harness = new DummyEngineHarness();

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal([new VoiceInfo("v1", "Voice One", "en-US"), new VoiceInfo("v2")],
            harness.VoiceStore.TryGetVoices(harness.Engine, "default"));
    }

    /* Voices without an id are dropped from the ListVoices reply */
    [Fact]
    public async Task ListVoices_WorkerVoiceWithoutId_IsDropped()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions
        {
            Voices = [new DummyVoice { Id = " " }, new DummyVoice { Id = "kept" }]
        });

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal([new VoiceInfo("kept")], engine.ListVoices());
    }

    /* A worker without voices yields an engine without voices and writes nothing to voices.json */
    [Fact]
    public async Task CreateAsync_WorkerHasNoVoices_DoesNotWriteVoicesFile()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { Voices = [] });

        using var engine = await CreateOrFailAsync(harness);

        Assert.Empty(engine.ListVoices());
        Assert.False(File.Exists(Path.Combine(harness.EngineDirectory, VoiceCatalogFile.FileName)));
    }

    /* A voices.json entry is used as-is and the worker is not asked for its voices */
    [Fact]
    public async Task CreateAsync_VoicesFileHasEntry_UsesItWithoutListVoices()
    {
        using var harness = new DummyEngineHarness();
        harness.VoiceStore.SaveVoices(harness.Engine, "default", [new VoiceInfo("shipped", "Shipped", "es-AR")]);

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal([new VoiceInfo("shipped", "Shipped", "es-AR")], engine.ListVoices());
        Assert.DoesNotContain(harness.ReadRequests(), r => r.Opcode == EngineOpcode.ListVoices);
    }

    /* A voices.json that cannot be written does not prevent the engine from starting */
    [Fact]
    public async Task CreateAsync_VoicesFileNotWritable_StillSucceeds()
    {
        using var harness = new DummyEngineHarness();
        Directory.CreateDirectory(Path.Combine(harness.EngineDirectory, VoiceCatalogFile.FileName));

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal(2, engine.ListVoices().Count);
    }

    /* Setting ActiveVoice persists it in the model's preferences */
    [Fact]
    public async Task ActiveVoice_Set_PersistsInModelConfig()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        engine.ActiveVoice = "v2";

        Assert.Equal("v2", harness.Repository.GetEngineConfig(harness.GetModel().Id).VoiceId);
        Assert.Equal(1, harness.Repository.EngineConfigSaveCount);
    }

    /* ActiveVoice reads the model's stored preference */
    [Fact]
    public async Task ActiveVoice_Get_ReadsModelConfig()
    {
        using var harness = new DummyEngineHarness();
        harness.Repository.GetEngineConfig(harness.GetModel().Id).VoiceId = "v2";

        using var engine = await CreateOrFailAsync(harness);

        Assert.Equal("v2", engine.ActiveVoice);
    }

    /* Setting Speed persists it in the model's preferences */
    [Fact]
    public async Task Speed_Set_PersistsInModelConfig()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        engine.Speed = 1.5f;

        Assert.Equal(1.5f, harness.Repository.GetEngineConfig(harness.GetModel().Id).Speed);
        Assert.Equal(1.5f, engine.Speed);
    }

    /* Preferences are kept per model: changing one model's voice leaves the other model untouched */
    [Fact]
    public async Task ActiveVoice_Set_OnlyAffectsItsOwnModel()
    {
        using var harness = new DummyEngineHarness(null,
            new EngineModelManifest { Id = "first" }, new EngineModelManifest { Id = "second" });
        using var engine = await CreateOrFailAsync(harness, "first");

        engine.ActiveVoice = "v2";

        Assert.Equal(string.Empty, harness.Repository.GetEngineConfig(harness.GetModel("second").Id).VoiceId);
    }

    /* The configured voice is sent when the model offers it */
    [Fact]
    public async Task SynthesizeStreamAsync_ConfiguredVoiceOffered_SendsConfiguredVoice()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);
        engine.ActiveVoice = "v2";

        await SynthesizeAllAsync(engine, "hello");

        Assert.Equal("v2", SynthesizeRequests(harness).Single().Json.GetProperty("voiceId").GetString());
    }

    /* A configured voice the model does not offer falls back to the model's first voice */
    [Fact]
    public async Task SynthesizeStreamAsync_ConfiguredVoiceNotOffered_FallsBackToFirstVoice()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);
        engine.ActiveVoice = "gone";

        await SynthesizeAllAsync(engine, "hello");

        Assert.Equal("v1", SynthesizeRequests(harness).Single().Json.GetProperty("voiceId").GetString());
    }

    /* The request carries the text and the model's configured speed */
    [Fact]
    public async Task SynthesizeStreamAsync_NoSpeedOverride_SendsTextAndConfiguredSpeed()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);
        engine.Speed = 1.25f;

        await SynthesizeAllAsync(engine, "hello world");

        var request = SynthesizeRequests(harness).Single().Json;
        Assert.Equal("hello world", request.GetProperty("text").GetString());
        Assert.Equal(1.25f, request.GetProperty("speed").GetSingle());
    }

    /* A speed override replaces the configured speed for that request */
    [Fact]
    public async Task SynthesizeStreamAsync_SpeedOverride_SendsOverride()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        await SynthesizeAllAsync(engine, "hello", speed: 0.75f);

        Assert.Equal(0.75f, SynthesizeRequests(harness).Single().Json.GetProperty("speed").GetSingle());
    }

    /* Every chunk the worker renders is yielded in order */
    [Fact]
    public async Task SynthesizeStreamAsync_Text_YieldsWorkerChunks()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 4, ChunkSize = 10 });
        using var engine = await CreateOrFailAsync(harness);

        var chunks = await SynthesizeAllAsync(engine, "hello");

        Assert.Equal(4, chunks.Count);
        Assert.Equal(Enumerable.Range(0, 4).Select(i => (byte)i), chunks.Select(c => c[0]));
    }

    /* Blank text yields nothing and is never sent to the worker */
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SynthesizeStreamAsync_BlankText_YieldsNothing(string text)
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        Assert.Empty(await SynthesizeAllAsync(engine, text));
        Assert.Empty(SynthesizeRequests(harness));
    }

    /* A model without voices yields nothing */
    [Fact]
    public async Task SynthesizeStreamAsync_NoVoices_YieldsNothing()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { Voices = [] });
        using var engine = await CreateOrFailAsync(harness);

        Assert.Empty(await SynthesizeAllAsync(engine, "hello"));
        Assert.Empty(SynthesizeRequests(harness));
    }

    /* A disposed engine yields nothing */
    [Fact]
    public async Task SynthesizeStreamAsync_Disposed_YieldsNothing()
    {
        using var harness = new DummyEngineHarness();
        var engine = await CreateOrFailAsync(harness);
        engine.Dispose();

        Assert.Empty(await SynthesizeAllAsync(engine, "hello"));
    }

    /* A worker error surfaces as EngineWorkerException from the stream */
    [Fact]
    public async Task SynthesizeStreamAsync_WorkerError_ThrowsEngineWorkerException()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        await Assert.ThrowsAsync<EngineWorkerException>(() => SynthesizeAllAsync(engine, harness.Options.ErrorOnText));
    }

    /* After the worker crashed, the next request restarts it and succeeds */
    [Fact]
    public async Task SynthesizeStreamAsync_AfterWorkerCrash_RestartsWorker()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { ChunksPerRequest = 2 });
        using var engine = await CreateOrFailAsync(harness);
        int firstPid = harness.ReadWorkerPid();
        await CrashWorkerAsync(harness, engine);

        var chunks = await SynthesizeAllAsync(engine, "hello");

        Assert.Equal(2, chunks.Count);
        Assert.NotEqual(firstPid, harness.ReadWorkerPid());
    }

    /* A restarted worker goes through the handshake again but voices are not requested twice */
    [Fact]
    public async Task SynthesizeStreamAsync_AfterWorkerCrash_RepeatsMetadataAndLoadModel()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);
        await CrashWorkerAsync(harness, engine);

        await SynthesizeAllAsync(engine, "hello");

        var opcodes = harness.ReadRequests().Select(r => r.Opcode).ToList();
        Assert.Equal(2, opcodes.Count(o => o == EngineOpcode.GetMetadata));
        Assert.Equal(2, opcodes.Count(o => o == EngineOpcode.LoadModel));
        Assert.Equal(1, opcodes.Count(o => o == EngineOpcode.ListVoices));
    }

    /* A crashed engine still reports itself available, since it recovers on the next request */
    [Fact]
    public async Task IsAvailable_AfterWorkerCrash_StaysTrue()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        await Assert.ThrowsAsync<EngineWorkerException>(() => SynthesizeAllAsync(engine, harness.Options.CrashOnText));

        Assert.True(engine.IsAvailable);
    }

    /* A restart whose handshake fails surfaces the worker's error */
    [Fact]
    public async Task SynthesizeStreamAsync_RestartFails_ThrowsEngineWorkerException()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);
        harness.UpdateOptions(o => o.FailLoadModel = true);
        await CrashWorkerAsync(harness, engine);

        var ex = await Assert.ThrowsAsync<EngineWorkerException>(() => SynthesizeAllAsync(engine, "hello"));

        Assert.Equal("model_load_failed", ex.Code);
    }

    /* SynthesizeAsync wraps the PCM in a WAV header describing the worker's format */
    [Fact]
    public async Task SynthesizeAsync_Text_ReturnsWavWithWorkerFormat()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { SampleRate = 16000, Channels = 2, BitDepth = 16, ChunksPerRequest = 3, ChunkSize = 40 });
        using var engine = await CreateOrFailAsync(harness);

        var result = await engine.SynthesizeAsync("hello", cancellationToken: Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        byte[] wav = result.Value!;
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(120, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(44 + 120, wav.Length);
    }

    /* SynthesizeAsync rejects blank text */
    [Fact]
    public async Task SynthesizeAsync_BlankText_ReturnsFailure()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        var result = await engine.SynthesizeAsync(" ", cancellationToken: Ct);

        Assert.True(result.IsFailure);
    }

    /* SynthesizeAsync on a disposed engine fails with InvalidEngineException */
    [Fact]
    public async Task SynthesizeAsync_Disposed_ReturnsInvalidEngineFailure()
    {
        using var harness = new DummyEngineHarness();
        var engine = await CreateOrFailAsync(harness);
        engine.Dispose();

        var result = await engine.SynthesizeAsync("hello", cancellationToken: Ct);

        Assert.IsType<InvalidEngineException>(result.Exception);
    }

    /* SynthesizeAsync without any voice fails with InvalidVoiceException */
    [Fact]
    public async Task SynthesizeAsync_NoVoices_ReturnsInvalidVoiceFailure()
    {
        using var harness = new DummyEngineHarness(new DummyEngineOptions { Voices = [] });
        using var engine = await CreateOrFailAsync(harness);

        var result = await engine.SynthesizeAsync("hello", cancellationToken: Ct);

        Assert.IsType<InvalidVoiceException>(result.Exception);
    }

    /* A worker error becomes a failed result */
    [Fact]
    public async Task SynthesizeAsync_WorkerError_ReturnsFailure()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);

        var result = await engine.SynthesizeAsync(harness.Options.ErrorOnText, cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.IsType<EngineWorkerException>(result.Exception);
    }

    /* A crash becomes a failed result and the following call succeeds on a restarted worker */
    [Fact]
    public async Task SynthesizeAsync_AfterCrash_NextCallSucceeds()
    {
        using var harness = new DummyEngineHarness();
        using var engine = await CreateOrFailAsync(harness);
        var crashed = await engine.SynthesizeAsync(harness.Options.CrashOnText, cancellationToken: Ct);

        var result = await engine.SynthesizeAsync("hello", cancellationToken: Ct);

        Assert.True(crashed.IsFailure);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    /* Dispose makes the engine unavailable */
    [Fact]
    public async Task Dispose_LiveEngine_MakesEngineUnavailable()
    {
        using var harness = new DummyEngineHarness();
        var engine = await CreateOrFailAsync(harness);

        engine.Dispose();

        Assert.False(engine.IsAvailable);
    }

    /* Dispose closes the worker's stdin so it exits gracefully */
    [Fact]
    public async Task Dispose_LiveEngine_WorkerExitsOnStdinClose()
    {
        using var harness = new DummyEngineHarness();
        var engine = await CreateOrFailAsync(harness);

        engine.Dispose();

        Assert.Contains("stdin-closed", harness.ReadExitLog());
    }

    /* Dispose can be called more than once */
    [Fact]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        using var harness = new DummyEngineHarness();
        var engine = await CreateOrFailAsync(harness);
        engine.Dispose();

        Assert.Null(Record.Exception(engine.Dispose));
    }
}
