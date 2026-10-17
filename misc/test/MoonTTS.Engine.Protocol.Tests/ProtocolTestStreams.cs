namespace MoonTTS.Engine.Protocol.Tests;

/* File Structure:
        - RecordingStream
            - Operations : List<string>
            - Written : byte[]
        - TrickleStream(byte[] data, int maxBytesPerRead)
        - HeaderThenZerosStream(byte[] header, long zeroCount)
*/

/* Write-only stream that records every write ("write:<n>") and flush ("flush") in order */
internal sealed class RecordingStream : Stream
{
    private readonly MemoryStream _buffer = new();

    public List<string> Operations { get; } = [];

    /* Every byte written so far */
    public byte[] Written => _buffer.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _buffer.Length;
    public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }

    /* Records a synchronous flush */
    public override void Flush() => Operations.Add("flush");

    /* Records an asynchronous flush */
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        Operations.Add("flush");
        return Task.CompletedTask;
    }

    /* Records a synchronous write and keeps its bytes */
    public override void Write(byte[] buffer, int offset, int count)
    {
        Operations.Add($"write:{count}");
        _buffer.Write(buffer, offset, count);
    }

    /* Records an asynchronous write and keeps its bytes */
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Operations.Add($"write:{buffer.Length}");
        _buffer.Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    /* Reading is not supported */
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /* Seeking is not supported */
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /* Resizing is not supported */
    public override void SetLength(long value) => throw new NotSupportedException();
}

/* Read-only stream that hands out at most a few bytes per read, like a pipe delivering a frame in pieces */
internal sealed class TrickleStream(byte[] data, int maxBytesPerRead) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    /* Copies up to maxBytesPerRead bytes */
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /* Copies up to maxBytesPerRead bytes */
    public override int Read(Span<byte> buffer)
    {
        int count = Math.Min(Math.Min(buffer.Length, maxBytesPerRead), data.Length - _position);
        data.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    /* Copies up to maxBytesPerRead bytes */
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    /* Nothing to flush on a read-only stream */
    public override void Flush() { }

    /* Seeking is not supported */
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /* Resizing is not supported */
    public override void SetLength(long value) => throw new NotSupportedException();

    /* Writing is not supported */
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/* Read-only stream that serves a header followed by a number of zero bytes without allocating them up front */
internal sealed class HeaderThenZerosStream(byte[] header, long zeroCount) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => header.Length + zeroCount;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    /* Serves header bytes first, then zeros, until the declared length is reached */
    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(buffer.Length, Length - _position);
        int fromHeader = (int)Math.Clamp(header.Length - _position, 0, count);
        if (fromHeader > 0)
            header.AsSpan((int)_position, fromHeader).CopyTo(buffer);
        buffer[fromHeader..count].Clear();
        _position += count;
        return count;
    }

    /* Serves header bytes first, then zeros */
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /* Serves header bytes first, then zeros */
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    /* Nothing to flush on a read-only stream */
    public override void Flush() { }

    /* Seeking is not supported */
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /* Resizing is not supported */
    public override void SetLength(long value) => throw new NotSupportedException();

    /* Writing is not supported */
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
