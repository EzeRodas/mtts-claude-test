using System.Text;
using System.Text.Json;

namespace MoonTTS.Engine.Protocol.Tests;

/* Verifies the binary framing (header layout, limits, partial reads) and the JSON helpers of EngineIpcProtocol */
public sealed class EngineIpcProtocolTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /* Builds a raw 8-byte header with the given fields */
    private static byte[] Header(byte opcode, uint length, byte flags = 0, byte magicFirst = 0x4D, byte magicSecond = 0x54)
    {
        byte[] header = [magicFirst, magicSecond, opcode, flags, 0, 0, 0, 0];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), length);
        return header;
    }

    /* Concatenates byte arrays into one stream */
    private static MemoryStream StreamOf(params byte[][] parts) => new(parts.SelectMany(p => p).ToArray());

    /* The header constants match the specification: "MT", 8 bytes, 32 MiB limit */
    [Fact]
    public void Constants_Always_MatchSpecification()
    {
        Assert.Equal(0x4D, EngineIpcProtocol.MagicFirst);
        Assert.Equal(0x54, EngineIpcProtocol.MagicSecond);
        Assert.Equal(8, EngineIpcProtocol.HeaderSize);
        Assert.Equal(32 * 1024 * 1024, EngineIpcProtocol.MaxPayloadSize);
    }

    /* A frame starts with the 'M' 'T' magic bytes */
    [Fact]
    public async Task WriteFrameAsync_AnyFrame_StartsWithMagic()
    {
        using var stream = new MemoryStream();

        await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.Ping, ReadOnlyMemory<byte>.Empty, Token);

        Assert.Equal((byte)'M', stream.ToArray()[0]);
        Assert.Equal((byte)'T', stream.ToArray()[1]);
    }

    /* The third header byte is the opcode */
    [Theory]
    [InlineData(EngineOpcode.Ping)]
    [InlineData(EngineOpcode.AudioChunk)]
    [InlineData(EngineOpcode.Error)]
    [InlineData(EngineOpcode.Shutdown)]
    public async Task WriteFrameAsync_Opcode_IsThirdHeaderByte(EngineOpcode opcode)
    {
        using var stream = new MemoryStream();

        await EngineIpcProtocol.WriteFrameAsync(stream, opcode, ReadOnlyMemory<byte>.Empty, Token);

        Assert.Equal((byte)opcode, stream.ToArray()[2]);
    }

    /* The flags byte is always written as zero */
    [Fact]
    public async Task WriteFrameAsync_AnyFrame_WritesZeroFlags()
    {
        using var stream = new MemoryStream();

        await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.AudioChunk, new byte[] { 1, 2, 3 }, Token);

        Assert.Equal(0, stream.ToArray()[3]);
    }

    /* The payload length is a big-endian uint32 in bytes 4..7 */
    [Theory]
    [InlineData(1, new byte[] { 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(258, new byte[] { 0x00, 0x00, 0x01, 0x02 })]
    [InlineData(0x010203, new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    public async Task WriteFrameAsync_PayloadLength_IsBigEndian(int length, byte[] expected)
    {
        using var stream = new MemoryStream();

        await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.AudioChunk, new byte[length], Token);

        Assert.Equal(expected, stream.ToArray()[4..8]);
    }

    /* The payload follows the header unchanged */
    [Fact]
    public async Task WriteFrameAsync_WithPayload_AppendsPayloadAfterHeader()
    {
        using var stream = new MemoryStream();
        byte[] payload = [10, 20, 30, 40, 50];

        await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.AudioChunk, payload, Token);

        Assert.Equal(EngineIpcProtocol.HeaderSize + payload.Length, stream.Length);
        Assert.Equal(payload, stream.ToArray()[EngineIpcProtocol.HeaderSize..]);
    }

    /* An empty payload produces a bare header with a zero length */
    [Fact]
    public async Task WriteFrameAsync_EmptyPayload_WritesHeaderOnly()
    {
        using var stream = new MemoryStream();

        await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.Ping, ReadOnlyMemory<byte>.Empty, Token);

        Assert.Equal(Header((byte)EngineOpcode.Ping, 0), stream.ToArray());
    }

    /* An empty payload issues no second write */
    [Fact]
    public async Task WriteFrameAsync_EmptyPayload_SkipsPayloadWrite()
    {
        var stream = new RecordingStream();

        await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.Ping, ReadOnlyMemory<byte>.Empty, Token);

        Assert.Equal(["write:8", "flush"], stream.Operations);
    }

    /* The frame is flushed once, after header and payload are written */
    [Fact]
    public async Task WriteFrameAsync_WithPayload_FlushesAfterWriting()
    {
        var stream = new RecordingStream();

        await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.AudioChunk, new byte[3], Token);

        Assert.Equal(["write:8", "write:3", "flush"], stream.Operations);
    }

    /* A payload of exactly the maximum size is accepted */
    [Fact]
    public async Task WriteFrameAsync_PayloadAtMaxSize_IsWritten()
    {
        var exception = await Record.ExceptionAsync(async () =>
            await EngineIpcProtocol.WriteFrameAsync(Stream.Null, EngineOpcode.AudioChunk, new byte[EngineIpcProtocol.MaxPayloadSize], Token));

        Assert.Null(exception);
    }

    /* A payload above the maximum size is rejected */
    [Fact]
    public async Task WriteFrameAsync_OversizedPayload_ThrowsInvalidOperationException()
    {
        var stream = new RecordingStream();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.AudioChunk, new byte[EngineIpcProtocol.MaxPayloadSize + 1], Token));
    }

    /* A rejected payload writes nothing to the stream */
    [Fact]
    public async Task WriteFrameAsync_OversizedPayload_WritesNothing()
    {
        var stream = new RecordingStream();

        await Record.ExceptionAsync(async () =>
            await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.AudioChunk, new byte[EngineIpcProtocol.MaxPayloadSize + 1], Token));

        Assert.Empty(stream.Operations);
    }

    /* A cancelled token aborts the write */
    [Fact]
    public async Task WriteFrameAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var stream = new MemoryStream();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await EngineIpcProtocol.WriteFrameAsync(stream, EngineOpcode.Ping, ReadOnlyMemory<byte>.Empty, cts.Token));
    }

    /* A written frame reads back with the same opcode and payload */
    [Theory]
    [InlineData(EngineOpcode.Ping)]
    [InlineData(EngineOpcode.Pong)]
    [InlineData(EngineOpcode.GetMetadata)]
    [InlineData(EngineOpcode.MetadataResponse)]
    [InlineData(EngineOpcode.ListVoices)]
    [InlineData(EngineOpcode.VoicesResponse)]
    [InlineData(EngineOpcode.LoadModel)]
    [InlineData(EngineOpcode.ModelLoaded)]
    [InlineData(EngineOpcode.Synthesize)]
    [InlineData(EngineOpcode.AudioChunk)]
    [InlineData(EngineOpcode.EndOfStream)]
    [InlineData(EngineOpcode.Cancel)]
    [InlineData(EngineOpcode.Error)]
    [InlineData(EngineOpcode.Shutdown)]
    public async Task ReadFrameAsync_WrittenFrame_RoundTrips(EngineOpcode opcode)
    {
        using var stream = new MemoryStream();
        byte[] payload = [1, 2, 3, 4, 5, 6, 7];
        await EngineIpcProtocol.WriteFrameAsync(stream, opcode, payload, Token);
        stream.Position = 0;

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.NotNull(frame);
        Assert.Equal(opcode, frame.Value.Opcode);
        Assert.Equal(payload, frame.Value.Payload.ToArray());
    }

    /* A zero-length frame reads back with an empty payload */
    [Fact]
    public async Task ReadFrameAsync_EmptyPayload_ReturnsEmptyPayload()
    {
        using var stream = StreamOf(Header((byte)EngineOpcode.Pong, 0));

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.NotNull(frame);
        Assert.Equal(EngineOpcode.Pong, frame.Value.Opcode);
        Assert.True(frame.Value.Payload.IsEmpty);
    }

    /* The flags byte of the header is exposed on the frame */
    [Fact]
    public async Task ReadFrameAsync_HeaderFlags_AreExposed()
    {
        using var stream = StreamOf(Header((byte)EngineOpcode.AudioChunk, 0, flags: 0x01));

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.Equal(0x01, frame!.Value.Flags);
    }

    /* An opcode byte outside the enum is passed through rather than rejected */
    [Fact]
    public async Task ReadFrameAsync_UnknownOpcode_IsPassedThrough()
    {
        using var stream = StreamOf(Header(0x77, 0));

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.Equal((EngineOpcode)0x77, frame!.Value.Opcode);
    }

    /* A stream that ends cleanly between frames yields null */
    [Fact]
    public async Task ReadFrameAsync_CleanEndOfStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.Null(frame);
    }

    /* Consecutive frames are read in order, then null at the end */
    [Fact]
    public async Task ReadFrameAsync_ConsecutiveFrames_ReadsEachThenNull()
    {
        using var stream = StreamOf(
            Header((byte)EngineOpcode.AudioChunk, 2), [9, 8],
            Header((byte)EngineOpcode.EndOfStream, 0));

        var first = await EngineIpcProtocol.ReadFrameAsync(stream, Token);
        var second = await EngineIpcProtocol.ReadFrameAsync(stream, Token);
        var third = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.Equal(EngineOpcode.AudioChunk, first!.Value.Opcode);
        Assert.Equal([9, 8], first.Value.Payload.ToArray());
        Assert.Equal(EngineOpcode.EndOfStream, second!.Value.Opcode);
        Assert.Null(third);
    }

    /* A frame delivered a byte (or a few) at a time is reassembled */
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ReadFrameAsync_FragmentedReads_ReassemblesFrame(int maxBytesPerRead)
    {
        byte[] payload = Encoding.UTF8.GetBytes("fragmented payload");
        var stream = new TrickleStream([.. Header((byte)EngineOpcode.AudioChunk, (uint)payload.Length), .. payload], maxBytesPerRead);

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.Equal(EngineOpcode.AudioChunk, frame!.Value.Opcode);
        Assert.Equal(payload, frame.Value.Payload.ToArray());
    }

    /* A stream that ends inside the header is reported as a truncated frame */
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public async Task ReadFrameAsync_PartialHeader_ThrowsEndOfStreamException(int headerBytes)
    {
        using var stream = new MemoryStream(Header((byte)EngineOpcode.Ping, 0)[..headerBytes]);

        await Assert.ThrowsAsync<EndOfStreamException>(async () => await EngineIpcProtocol.ReadFrameAsync(stream, Token));
    }

    /* A stream that ends inside the payload is reported as a truncated frame */
    [Fact]
    public async Task ReadFrameAsync_TruncatedPayload_ThrowsEndOfStreamException()
    {
        using var stream = StreamOf(Header((byte)EngineOpcode.AudioChunk, 10), [1, 2, 3, 4]);

        await Assert.ThrowsAsync<EndOfStreamException>(async () => await EngineIpcProtocol.ReadFrameAsync(stream, Token));
    }

    /* A header without the "MT" magic means the stream is out of sync */
    [Theory]
    [InlineData(0x00, 0x54)]
    [InlineData(0x4D, 0x00)]
    [InlineData(0x54, 0x4D)]
    public async Task ReadFrameAsync_BadMagic_ThrowsInvalidDataException(byte first, byte second)
    {
        using var stream = StreamOf(Header((byte)EngineOpcode.Ping, 0, magicFirst: first, magicSecond: second));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await EngineIpcProtocol.ReadFrameAsync(stream, Token));
    }

    /* A declared length above the maximum is rejected before any allocation */
    [Theory]
    [InlineData(32u * 1024 * 1024 + 1)]
    [InlineData(uint.MaxValue)]
    public async Task ReadFrameAsync_OversizedLength_ThrowsInvalidDataException(uint length)
    {
        using var stream = StreamOf(Header((byte)EngineOpcode.AudioChunk, length));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await EngineIpcProtocol.ReadFrameAsync(stream, Token));
    }

    /* A declared length of exactly the maximum is accepted */
    [Fact]
    public async Task ReadFrameAsync_LengthAtMaxSize_ReadsPayload()
    {
        var stream = new HeaderThenZerosStream(
            Header((byte)EngineOpcode.AudioChunk, EngineIpcProtocol.MaxPayloadSize), EngineIpcProtocol.MaxPayloadSize);

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);

        Assert.Equal(EngineIpcProtocol.MaxPayloadSize, frame!.Value.Payload.Length);
    }

    /* A cancelled token aborts the read */
    [Fact]
    public async Task ReadFrameAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var stream = StreamOf(Header((byte)EngineOpcode.Ping, 0));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await EngineIpcProtocol.ReadFrameAsync(stream, cts.Token));
    }

    /* The JSON frame payload is exactly the source-generated serialization of the message */
    [Fact]
    public async Task WriteJsonFrameAsync_Message_WritesSerializedPayload()
    {
        using var stream = new MemoryStream();
        var request = new SynthesizeRequest { Text = "Hello", VoiceId = "af_heart", Speed = 1.5f };
        byte[] expected = JsonSerializer.SerializeToUtf8Bytes(request, EngineProtocolJsonContext.Default.SynthesizeRequest);

        await EngineIpcProtocol.WriteJsonFrameAsync(stream, EngineOpcode.Synthesize, request, EngineProtocolJsonContext.Default.SynthesizeRequest, Token);

        Assert.Equal(Header((byte)EngineOpcode.Synthesize, (uint)expected.Length), stream.ToArray()[..8]);
        Assert.Equal(expected, stream.ToArray()[8..]);
    }

    /* A JSON frame reads back into an equivalent message */
    [Fact]
    public async Task WriteJsonFrameAsync_ThenReadJson_RoundTrips()
    {
        using var stream = new MemoryStream();
        var request = new SynthesizeRequest { Text = "Hello", VoiceId = "af_heart", Speed = 1.5f, Pitch = 0.5f };
        await EngineIpcProtocol.WriteJsonFrameAsync(stream, EngineOpcode.Synthesize, request, EngineProtocolJsonContext.Default.SynthesizeRequest, Token);
        stream.Position = 0;

        var frame = await EngineIpcProtocol.ReadFrameAsync(stream, Token);
        var read = EngineIpcProtocol.ReadJson(frame!.Value, EngineProtocolJsonContext.Default.SynthesizeRequest);

        Assert.Equal(EngineOpcode.Synthesize, frame.Value.Opcode);
        Assert.Equal("Hello", read.Text);
        Assert.Equal("af_heart", read.VoiceId);
        Assert.Equal(1.5f, read.Speed);
        Assert.Equal(0.5f, read.Pitch);
    }

    /* ReadJson deserializes the payload of a control frame */
    [Fact]
    public void ReadJson_ValidPayload_ReturnsMessage()
    {
        var frame = new EngineFrame(EngineOpcode.Error, 0, Encoding.UTF8.GetBytes("""{"code":"model_not_found","message":"No .onnx file was found."}"""));

        var error = EngineIpcProtocol.ReadJson(frame, EngineProtocolJsonContext.Default.EngineErrorMessage);

        Assert.Equal("model_not_found", error.Code);
        Assert.Equal("No .onnx file was found.", error.Message);
    }

    /* A literal "null" payload is rejected as an empty message */
    [Fact]
    public void ReadJson_NullLiteralPayload_ThrowsInvalidDataException()
    {
        var frame = new EngineFrame(EngineOpcode.Error, 0, Encoding.UTF8.GetBytes("null"));

        Assert.Throws<InvalidDataException>(() => EngineIpcProtocol.ReadJson(frame, EngineProtocolJsonContext.Default.EngineErrorMessage));
    }

    /* An empty payload fails with one of the two exception types the host handles (JsonException or InvalidDataException) */
    [Fact]
    public void ReadJson_EmptyPayload_ThrowsHandledDeserializationError()
    {
        var frame = new EngineFrame(EngineOpcode.Error, 0, ReadOnlyMemory<byte>.Empty);

        var exception = Record.Exception(() => EngineIpcProtocol.ReadJson(frame, EngineProtocolJsonContext.Default.EngineErrorMessage));

        Assert.True(exception is JsonException or InvalidDataException, $"Unexpected exception: {exception?.GetType()}");
    }

    /* A payload that is not JSON fails with a JsonException */
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"code\":")]
    [InlineData("[1,2,3]")]
    public void ReadJson_MalformedPayload_ThrowsJsonException(string payload)
    {
        var frame = new EngineFrame(EngineOpcode.Error, 0, Encoding.UTF8.GetBytes(payload));

        Assert.ThrowsAny<JsonException>(() => EngineIpcProtocol.ReadJson(frame, EngineProtocolJsonContext.Default.EngineErrorMessage));
    }
}
