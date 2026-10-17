using System.Buffers.Binary;
using System.Text;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;

namespace MoonTTS.Core.Tests.Utilities;

/* Tests for WavWriter.CreateWav and WavWriter.WriteWavHeader */
public sealed class WavWriterTests
{
    private static readonly AudioFormat StereoFormat = new(48000, 2, 16);

    /* The WAV is exactly one 44-byte header plus the PCM */
    [Fact]
    public void CreateWav_WithPcm_LengthIsHeaderPlusPcm()
    {
        byte[] wav = WavWriter.CreateWav(new byte[100], StereoFormat);

        Assert.Equal(WavWriter.HeaderSize + 100, wav.Length);
    }

    /* The PCM bytes are copied unchanged right after the header */
    [Fact]
    public void CreateWav_WithPcm_CopiesPcmAfterHeader()
    {
        byte[] pcm = [1, 2, 3, 4, 5, 6];

        byte[] wav = WavWriter.CreateWav(pcm, StereoFormat);

        Assert.Equal(pcm, wav[WavWriter.HeaderSize..]);
    }

    /* Empty PCM produces a bare header declaring zero data bytes */
    [Fact]
    public void CreateWav_EmptyPcm_ReturnsHeaderWithZeroDataSize()
    {
        byte[] wav = WavWriter.CreateWav(ReadOnlySpan<byte>.Empty, StereoFormat);

        Assert.Equal(WavWriter.HeaderSize, wav.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40, 4)));
    }

    /* The RIFF, WAVE, fmt and data chunk identifiers are at their standard offsets */
    [Fact]
    public void WriteWavHeader_Always_WritesChunkIdentifiers()
    {
        byte[] header = new byte[WavWriter.HeaderSize];

        WavWriter.WriteWavHeader(header, 10, StereoFormat);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(header, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(header, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(header, 36, 4));
    }

    /* The RIFF size is 36 + PCM length and the data size is the PCM length */
    [Fact]
    public void WriteWavHeader_PcmLength_WritesRiffAndDataSizes()
    {
        byte[] header = new byte[WavWriter.HeaderSize];

        WavWriter.WriteWavHeader(header, 1234, StereoFormat);

        Assert.Equal(36 + 1234, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4)));
        Assert.Equal(1234, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(40, 4)));
    }

    /* The fmt chunk declares 16-byte PCM (format tag 1) */
    [Fact]
    public void WriteWavHeader_Always_DeclaresPcmFmtChunk()
    {
        byte[] header = new byte[WavWriter.HeaderSize];

        WavWriter.WriteWavHeader(header, 0, StereoFormat);

        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(20, 2)));
    }

    /* Channels, sample rate, byte rate, block align and bit depth all come from the given format */
    [Theory]
    [InlineData(22050, 1, 16)]
    [InlineData(48000, 2, 16)]
    [InlineData(44100, 2, 24)]
    [InlineData(8000, 1, 8)]
    public void WriteWavHeader_Format_WritesFormatFields(int sampleRate, short channels, short bitDepth)
    {
        var format = new AudioFormat(sampleRate, channels, bitDepth);
        byte[] header = new byte[WavWriter.HeaderSize];

        WavWriter.WriteWavHeader(header, 0, format);

        Assert.Equal(channels, BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(22, 2)));
        Assert.Equal(sampleRate, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24, 4)));
        Assert.Equal(format.BytesPerSecond, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(28, 4)));
        Assert.Equal(format.FrameSize, BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(32, 2)));
        Assert.Equal(bitDepth, BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(34, 2)));
    }

    /* A span shorter than the header is rejected instead of being partially written */
    [Fact]
    public void WriteWavHeader_SpanTooSmall_Throws()
    {
        byte[] header = new byte[WavWriter.HeaderSize - 1];

        Assert.ThrowsAny<ArgumentException>(() => WavWriter.WriteWavHeader(header, 0, StereoFormat));
    }
}
