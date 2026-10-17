namespace MoonTTS.Tests.DummyEngine;

/* Behaviour of the dummy worker, read from "dummy-engine.json" in its working directory (the engine folder) */
public sealed class DummyEngineOptions
{
    public const string FileName = "dummy-engine.json";

    /* Identity and audio format reported in MetadataResponse */
    public string EngineId { get; set; } = "dummy";
    public int SampleRate { get; set; } = 22050;
    public short Channels { get; set; } = 1;
    public short BitDepth { get; set; } = 16;

    /* Voices returned by ListVoices */
    public List<DummyVoice> Voices { get; set; } = [new() { Id = "v1", Name = "Voice One", Language = "en-US" }, new() { Id = "v2" }];

    /* Synthesize produces ChunksPerRequest chunks of ChunkSize bytes, ChunkDelayMs apart.
       Byte values are the chunk index so the host side can verify ordering */
    public int ChunksPerRequest { get; set; } = 3;
    public int ChunkSize { get; set; } = 960;
    public int ChunkDelayMs { get; set; }

    /* Failure modes */
    public bool FailLoadModel { get; set; }
    public bool HangOnMetadata { get; set; }
    public bool GarbageOnMetadata { get; set; }
    public bool IgnoreCancel { get; set; }

    /* Text that makes the worker exit with code 3 mid-request, or reply with an Error frame */
    public string CrashOnText { get; set; } = "crash";
    public string ErrorOnText { get; set; } = "error";

    /* When set, every received frame is appended to this file as "<Opcode> <payload>" */
    public string? RequestLogPath { get; set; }
}

/* A voice reported by ListVoices */
public sealed class DummyVoice
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Language { get; set; }
}
