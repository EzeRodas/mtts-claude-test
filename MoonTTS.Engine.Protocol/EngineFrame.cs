namespace MoonTTS.Engine.Protocol;

/* A single decoded protocol frame. The payload is raw PCM for AudioChunk and UTF-8 JSON for control messages */
public readonly record struct EngineFrame(EngineOpcode Opcode, byte Flags, ReadOnlyMemory<byte> Payload);
