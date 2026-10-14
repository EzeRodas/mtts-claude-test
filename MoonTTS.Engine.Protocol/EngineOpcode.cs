namespace MoonTTS.Engine.Protocol;

/* Command / response types exchanged between the host and an engine worker.
   Every Host -> Worker request is answered by exactly one terminal Worker -> Host frame
   (its response opcode, EndOfStream for Synthesize, or Error) */
public enum EngineOpcode : byte
{
    /* Liveness */
    Ping = 0x01,
    Pong = 0x02,

    /* Discovery */
    GetMetadata = 0x10,
    MetadataResponse = 0x11,
    ListVoices = 0x12,
    VoicesResponse = 0x13,
    LoadModel = 0x14,
    ModelLoaded = 0x15,

    /* Synthesis */
    Synthesize = 0x20,
    AudioChunk = 0x21,
    EndOfStream = 0x22,
    Cancel = 0x23,

    /* Control */
    Error = 0xFE,
    Shutdown = 0xFF
}
