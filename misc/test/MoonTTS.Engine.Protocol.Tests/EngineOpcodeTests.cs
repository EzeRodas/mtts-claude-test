namespace MoonTTS.Engine.Protocol.Tests;

/* Pins every opcode to the byte value given in IPC-engine-model.md (section 2.2), which third-party workers rely on */
public sealed class EngineOpcodeTests
{
    /* Each opcode has the wire value from the specification table */
    [Theory]
    [InlineData(EngineOpcode.Ping, 0x01)]
    [InlineData(EngineOpcode.Pong, 0x02)]
    [InlineData(EngineOpcode.GetMetadata, 0x10)]
    [InlineData(EngineOpcode.MetadataResponse, 0x11)]
    [InlineData(EngineOpcode.ListVoices, 0x12)]
    [InlineData(EngineOpcode.VoicesResponse, 0x13)]
    [InlineData(EngineOpcode.LoadModel, 0x14)]
    [InlineData(EngineOpcode.ModelLoaded, 0x15)]
    [InlineData(EngineOpcode.Synthesize, 0x20)]
    [InlineData(EngineOpcode.AudioChunk, 0x21)]
    [InlineData(EngineOpcode.EndOfStream, 0x22)]
    [InlineData(EngineOpcode.Cancel, 0x23)]
    [InlineData(EngineOpcode.Error, 0xFE)]
    [InlineData(EngineOpcode.Shutdown, 0xFF)]
    public void Value_Opcode_MatchesSpecification(EngineOpcode opcode, byte expected)
    {
        Assert.Equal(expected, (byte)opcode);
    }

    /* The enum defines exactly the 14 opcodes of the specification */
    [Fact]
    public void Values_Always_HaveFourteenOpcodes()
    {
        Assert.Equal(14, Enum.GetValues<EngineOpcode>().Length);
    }

    /* The opcode is a single byte on the wire */
    [Fact]
    public void UnderlyingType_Always_IsByte()
    {
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(EngineOpcode)));
    }
}
