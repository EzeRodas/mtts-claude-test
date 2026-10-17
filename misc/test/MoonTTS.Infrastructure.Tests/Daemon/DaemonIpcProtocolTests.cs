using System.Buffers.Binary;
using MoonTTS.Infrastructure.Daemon.DTOs;

namespace MoonTTS.Infrastructure.Tests.Daemon;

/* DaemonIpcProtocol's 5-byte binary framing ([opcode][big-endian int32 length][payload]) and pipe naming */
public sealed class DaemonIpcProtocolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* A stream holding exactly the given bytes */
    private static MemoryStream StreamOf(params byte[] bytes) => new(bytes);

    /* A 5-byte header with an arbitrary length field */
    private static byte[] Header(DaemonOpcode opcode, int length)
    {
        byte[] header = new byte[DaemonIpcProtocol.HeaderSize];
        header[0] = (byte)opcode;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), length);
        return header;
    }

    /* The pipe name is per user so users never share a daemon */
    [Fact]
    public void PipeName_Always_ContainsUserName()
    {
        Assert.Equal($"Moon-TTS_Daemon_{Environment.UserName}", DaemonIpcProtocol.PipeName);
    }

    /* WriteFrameAsync writes the opcode, the big-endian length and the payload */
    [Fact]
    public async Task WriteFrameAsync_Payload_WritesHeaderThenPayload()
    {
        using var stream = new MemoryStream();

        await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioChunk, new byte[] { 9, 8, 7 }, Ct);

        Assert.Equal([0x11, 0, 0, 0, 3, 9, 8, 7], stream.ToArray());
    }

    /* An empty payload writes only the header */
    [Fact]
    public async Task WriteFrameAsync_EmptyPayload_WritesOnlyHeader()
    {
        using var stream = new MemoryStream();

        await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioEnd, ReadOnlyMemory<byte>.Empty, Ct);

        Assert.Equal([0x12, 0, 0, 0, 0], stream.ToArray());
    }

    /* A written frame reads back with the same opcode and payload */
    [Fact]
    public async Task ReadFrameAsync_WrittenFrame_RoundTrips()
    {
        using var stream = new MemoryStream();
        byte[] payload = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.Response, payload, Ct);
        stream.Position = 0;

        var (opcode, read) = await DaemonIpcProtocol.ReadFrameAsync(stream, Ct);

        Assert.Equal(DaemonOpcode.Response, opcode);
        Assert.Equal(payload, read);
    }

    /* Consecutive frames are read in order */
    [Fact]
    public async Task ReadFrameAsync_ConsecutiveFrames_ReadsEachInOrder()
    {
        using var stream = new MemoryStream();
        await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioChunk, new byte[] { 1 }, Ct);
        await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.AudioEnd, ReadOnlyMemory<byte>.Empty, Ct);
        stream.Position = 0;

        var first = await DaemonIpcProtocol.ReadFrameAsync(stream, Ct);
        var second = await DaemonIpcProtocol.ReadFrameAsync(stream, Ct);

        Assert.Equal((DaemonOpcode.AudioChunk, 1), (first.Opcode, first.Payload.Length));
        Assert.Equal((DaemonOpcode.AudioEnd, 0), (second.Opcode, second.Payload.Length));
    }

    /* A zero-length frame yields an empty payload */
    [Fact]
    public async Task ReadFrameAsync_ZeroLength_ReturnsEmptyPayload()
    {
        using var stream = StreamOf(Header(DaemonOpcode.Cancel, 0));

        var (opcode, payload) = await DaemonIpcProtocol.ReadFrameAsync(stream, Ct);

        Assert.Equal(DaemonOpcode.Cancel, opcode);
        Assert.Empty(payload);
    }

    /* A negative length field is rejected */
    [Fact]
    public async Task ReadFrameAsync_NegativeLength_ThrowsInvalidOperation()
    {
        using var stream = StreamOf(Header(DaemonOpcode.AudioChunk, -1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DaemonIpcProtocol.ReadFrameAsync(stream, Ct).AsTask());
    }

    /* A length above the 32 MB guard is rejected before allocating */
    [Fact]
    public async Task ReadFrameAsync_LengthAboveMaximum_ThrowsInvalidOperation()
    {
        using var stream = StreamOf(Header(DaemonOpcode.AudioChunk, DaemonIpcProtocol.MaxPayloadSize + 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DaemonIpcProtocol.ReadFrameAsync(stream, Ct).AsTask());
    }

    /* A stream that ends inside the header raises EndOfStreamException */
    [Fact]
    public async Task ReadFrameAsync_TruncatedHeader_ThrowsEndOfStream()
    {
        using var stream = StreamOf(0x11, 0, 0);

        await Assert.ThrowsAsync<EndOfStreamException>(() => DaemonIpcProtocol.ReadFrameAsync(stream, Ct).AsTask());
    }

    /* An empty stream raises EndOfStreamException */
    [Fact]
    public async Task ReadFrameAsync_EmptyStream_ThrowsEndOfStream()
    {
        using var stream = StreamOf();

        await Assert.ThrowsAsync<EndOfStreamException>(() => DaemonIpcProtocol.ReadFrameAsync(stream, Ct).AsTask());
    }

    /* A stream that ends inside the payload raises EndOfStreamException */
    [Fact]
    public async Task ReadFrameAsync_TruncatedPayload_ThrowsEndOfStream()
    {
        using var stream = StreamOf([.. Header(DaemonOpcode.AudioChunk, 10), 1, 2, 3]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => DaemonIpcProtocol.ReadFrameAsync(stream, Ct).AsTask());
    }

    /* A cancelled token stops the read */
    [Fact]
    public async Task ReadFrameAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var stream = StreamOf(Header(DaemonOpcode.AudioEnd, 0));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DaemonIpcProtocol.ReadFrameAsync(stream, new CancellationToken(canceled: true)).AsTask());
    }
}
