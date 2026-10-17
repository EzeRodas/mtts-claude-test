namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the stored values and record semantics of EngineFrame */
public sealed class EngineFrameTests
{
    /* The constructor stores opcode, flags and payload */
    [Fact]
    public void Constructor_Always_StoresComponents()
    {
        byte[] payload = [1, 2, 3];

        var frame = new EngineFrame(EngineOpcode.AudioChunk, 0x01, payload);

        Assert.Equal(EngineOpcode.AudioChunk, frame.Opcode);
        Assert.Equal(0x01, frame.Flags);
        Assert.Equal(payload, frame.Payload.ToArray());
    }

    /* The default frame has no payload */
    [Fact]
    public void Default_Always_HasEmptyPayload()
    {
        Assert.True(default(EngineFrame).Payload.IsEmpty);
    }

    /* Frames over the same payload memory with the same header fields are equal */
    [Fact]
    public void Equality_SameComponents_AreEqual()
    {
        byte[] payload = [1, 2, 3];

        Assert.Equal(new EngineFrame(EngineOpcode.Pong, 0, payload), new EngineFrame(EngineOpcode.Pong, 0, payload));
    }

    /* Frames with a different opcode or flags are not equal */
    [Theory]
    [InlineData(EngineOpcode.Ping, 0)]
    [InlineData(EngineOpcode.Pong, 1)]
    public void Equality_DifferentHeader_AreNotEqual(EngineOpcode opcode, byte flags)
    {
        byte[] payload = [1, 2, 3];

        Assert.NotEqual(new EngineFrame(EngineOpcode.Pong, 0, payload), new EngineFrame(opcode, flags, payload));
    }

    /* A with-expression replaces only the given component */
    [Fact]
    public void With_ChangedOpcode_KeepsFlagsAndPayload()
    {
        byte[] payload = [1, 2, 3];
        var original = new EngineFrame(EngineOpcode.AudioChunk, 0x01, payload);

        var copy = original with { Opcode = EngineOpcode.EndOfStream };

        Assert.Equal(EngineOpcode.EndOfStream, copy.Opcode);
        Assert.Equal(original.Flags, copy.Flags);
        Assert.Equal(payload, copy.Payload.ToArray());
    }
}
