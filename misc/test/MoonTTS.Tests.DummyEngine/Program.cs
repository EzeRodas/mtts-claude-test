using System.Text;
using System.Text.Json;
using MoonTTS.Engine.Protocol;
using MoonTTS.Tests.DummyEngine;

/* Dummy engine worker: answers the Moon-TTS engine protocol according to dummy-engine.json.
   Synthesis runs on a background task so Cancel frames are read while chunks are being sent */

var options = File.Exists(DummyEngineOptions.FileName)
    ? JsonSerializer.Deserialize<DummyEngineOptions>(File.ReadAllText(DummyEngineOptions.FileName)) ?? new DummyEngineOptions()
    : new DummyEngineOptions();

if (options.PidFilePath != null)
    File.WriteAllText(options.PidFilePath, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
Console.SetOut(Console.Error);

var json = EngineProtocolJsonContext.Default;
var writeLock = new SemaphoreSlim(1, 1);
CancellationTokenSource? activeCts = null;
Task? activeJob = null;

/* Writes one frame under the write lock */
async Task Send(EngineOpcode opcode, ReadOnlyMemory<byte> payload)
{
    await writeLock.WaitAsync();
    try { await EngineIpcProtocol.WriteFrameAsync(output, opcode, payload); }
    finally { writeLock.Release(); }
}

/* Writes an Error frame */
Task SendError(string code, string message) =>
    Send(EngineOpcode.Error, JsonSerializer.SerializeToUtf8Bytes(new EngineErrorMessage { Code = code, Message = message }, json.EngineErrorMessage));

/* Records why the frame loop ended, so tests can tell a graceful exit from a kill */
void LogExit(string reason)
{
    if (options.ExitLogPath != null)
        File.AppendAllText(options.ExitLogPath, reason + "\n");
}

/* Streams the scripted chunks for one Synthesize request */
async Task Synthesize(SynthesizeRequest request, CancellationToken token)
{
    if (request.Text == options.CrashOnText)
    {
        Console.Error.WriteLine("dummy engine crashing on purpose");
        Environment.Exit(3);
    }
    if (request.Text == options.ErrorOnText)
    {
        await SendError("synthesis_failed", "dummy synthesis error");
        return;
    }
    if (request.Text == options.UnexpectedOpcodeOnText)
    {
        await Send(EngineOpcode.Pong, ReadOnlyMemory<byte>.Empty);
        return;
    }
    if (request.Text == options.UnreadableErrorOnText)
    {
        await Send(EngineOpcode.Error, "this is not json"u8.ToArray());
        return;
    }

    for (int i = 0; i < options.ChunksPerRequest; i++)
    {
        if (token.IsCancellationRequested && !options.IgnoreCancel)
            break;
        if (options.CrashAfterChunks == i)
        {
            Console.Error.WriteLine("dummy engine crashing mid-stream on purpose");
            Environment.Exit(3);
        }
        await Send(EngineOpcode.AudioChunk, Enumerable.Repeat((byte)i, options.ChunkSize).ToArray());
        if (options.ChunkDelayMs > 0)
            await Task.Delay(options.ChunkDelayMs);
    }

    if (options.IgnoreCancel && token.IsCancellationRequested)
        await Task.Delay(Timeout.Infinite);

    await Send(EngineOpcode.EndOfStream, ReadOnlyMemory<byte>.Empty);
}

while (await EngineIpcProtocol.ReadFrameAsync(input) is { } frame)
{
    if (options.RequestLogPath != null)
        File.AppendAllText(options.RequestLogPath, $"{frame.Opcode} {Encoding.UTF8.GetString(frame.Payload.Span)}\n");

    switch (frame.Opcode)
    {
        case EngineOpcode.Ping:
            await Send(EngineOpcode.Pong, ReadOnlyMemory<byte>.Empty);
            break;

        case EngineOpcode.GetMetadata:
            if (options.HangOnMetadata)
            {
                await Task.Delay(Timeout.Infinite);
            }
            if (options.GarbageOnMetadata)
            {
                await output.WriteAsync("this is not a frame"u8.ToArray());
                await output.FlushAsync();
                break;
            }
            await Send(EngineOpcode.MetadataResponse, JsonSerializer.SerializeToUtf8Bytes(new EngineMetadataMessage
            {
                EngineId = options.ReportedEngineId ?? options.EngineId,
                EngineName = "Dummy Engine",
                Version = "1.0.0",
                AudioFormat = new EngineAudioFormat { SampleRate = options.SampleRate, Channels = options.Channels, BitDepth = options.BitDepth },
                SupportsStreaming = true,
                SupportsSpeedAdjustment = true
            }, json.EngineMetadataMessage));
            break;

        case EngineOpcode.LoadModel:
            if (options.FailLoadModel)
                await SendError("model_load_failed", "dummy model load failure");
            else
                await Send(EngineOpcode.ModelLoaded, ReadOnlyMemory<byte>.Empty);
            break;

        case EngineOpcode.ListVoices:
            await Send(EngineOpcode.VoicesResponse, JsonSerializer.SerializeToUtf8Bytes(new VoicesResponse
            {
                Voices = options.Voices.Select(v => new VoiceEntry { Id = v.Id, Name = v.Name, Language = v.Language }).ToList()
            }, json.VoicesResponse));
            break;

        case EngineOpcode.Synthesize:
            var request = EngineIpcProtocol.ReadJson(frame, json.SynthesizeRequest);
            activeCts?.Dispose();
            activeCts = new CancellationTokenSource();
            var token = activeCts.Token;
            activeJob = Task.Run(() => Synthesize(request, token));
            break;

        case EngineOpcode.Cancel:
            activeCts?.Cancel();
            break;

        case EngineOpcode.Shutdown:
            LogExit("shutdown");
            return 0;

        default:
            await SendError("unsupported_opcode", frame.Opcode.ToString());
            break;
    }
}

LogExit("stdin-closed");
if (options.HangOnStdinClose)
    await Task.Delay(Timeout.Infinite);

if (activeJob != null)
    await activeJob;
return 0;
