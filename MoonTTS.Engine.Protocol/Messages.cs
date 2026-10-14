namespace MoonTTS.Engine.Protocol;

/* JSON payloads carried by control frames. Property names are serialized in camelCase */

/* 0x11 MetadataResponse: engine identity and the PCM format it emits */
public sealed class EngineMetadataMessage
{
    public string EngineId { get; set; } = string.Empty;
    public string EngineName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Author { get; set; }
    public EngineAudioFormat AudioFormat { get; set; } = new();
    public bool SupportsStreaming { get; set; }
    public bool SupportsSpeedAdjustment { get; set; }
    public bool SupportsPitchAdjustment { get; set; }
}

/* PCM format description used inside MetadataResponse. Encoding is always little-endian signed PCM */
public sealed class EngineAudioFormat
{
    public int SampleRate { get; set; }
    public short Channels { get; set; }
    public short BitDepth { get; set; }
    public string Encoding { get; set; } = "PCM_SIGNED";
}

/* 0x14 LoadModel: tells the worker which model to load and where its downloaded files live */
public sealed class LoadModelRequest
{
    public string ModelId { get; set; } = string.Empty;
    public string ModelDirectory { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
}

/* 0x13 VoicesResponse: voices available for the loaded model */
public sealed class VoicesResponse
{
    public List<VoiceEntry> Voices { get; set; } = new();
}

/* Describes one voice. Also the element type of voices.json */
public sealed class VoiceEntry
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Language { get; set; }
}

/* 0x20 Synthesize: one synthesis job. The worker answers with AudioChunk frames followed by EndOfStream */
public sealed class SynthesizeRequest
{
    public string Text { get; set; } = string.Empty;
    public string VoiceId { get; set; } = string.Empty;
    public float Speed { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
}

/* 0xFE Error: terminal reply for a failed request */
public sealed class EngineErrorMessage
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
