using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;

namespace MoonTTS.Core.Tests.Utilities;

/* Tests for both FormatReader.GetFormatFromWavHeader overloads */
public sealed class FormatReaderTests
{
    /* The byte[] overload reads back the format written by WavWriter */
    [Theory]
    [InlineData(22050, 1, 16)]
    [InlineData(48000, 2, 24)]
    [InlineData(8000, 1, 8)]
    public void GetFormatFromWavHeader_ByteArrayWavHeader_ReturnsWrittenFormat(int sampleRate, short channels, short bitDepth)
    {
        var format = new AudioFormat(sampleRate, channels, bitDepth);
        byte[] wav = WavWriter.CreateWav(new byte[8], format);

        AudioFormat result = FormatReader.GetFormatFromWavHeader(wav);

        Assert.Equal(format, result);
    }

    /* The span overload reads back the format written by WavWriter */
    [Theory]
    [InlineData(22050, 1, 16)]
    [InlineData(48000, 2, 24)]
    [InlineData(8000, 1, 8)]
    public void GetFormatFromWavHeader_SpanWavHeader_ReturnsWrittenFormat(int sampleRate, short channels, short bitDepth)
    {
        var format = new AudioFormat(sampleRate, channels, bitDepth);
        byte[] wav = WavWriter.CreateWav(new byte[8], format);

        AudioFormat result = FormatReader.GetFormatFromWavHeader(wav.AsSpan());

        Assert.Equal(format, result);
    }

    /* 36 bytes are enough for the byte[] overload: the bit depth ends at offset 36 */
    [Fact]
    public void GetFormatFromWavHeader_ByteArrayOf36Bytes_ReadsFormat()
    {
        var format = new AudioFormat(16000, 2, 16);
        byte[] header = WavWriter.CreateWav(ReadOnlySpan<byte>.Empty, format)[..36];

        AudioFormat result = FormatReader.GetFormatFromWavHeader(header);

        Assert.Equal(format, result);
    }

    /* 36 bytes are enough for the span overload: the bit depth ends at offset 36 */
    [Fact]
    public void GetFormatFromWavHeader_SpanOf36Bytes_ReadsFormat()
    {
        var format = new AudioFormat(16000, 2, 16);
        byte[] header = WavWriter.CreateWav(ReadOnlySpan<byte>.Empty, format)[..36];

        AudioFormat result = FormatReader.GetFormatFromWavHeader(header.AsSpan());

        Assert.Equal(format, result);
    }

    /* A null array does not throw and yields a usable (non-zero) placeholder format */
    [Fact]
    public void GetFormatFromWavHeader_NullByteArray_ReturnsUsableFormat()
    {
        AudioFormat result = FormatReader.GetFormatFromWavHeader((byte[])null!);

        AssertUsable(result);
    }

    /* An array too short to hold a header does not throw and yields a usable placeholder format */
    [Fact]
    public void GetFormatFromWavHeader_ShortByteArray_ReturnsUsableFormat()
    {
        AudioFormat result = FormatReader.GetFormatFromWavHeader(new byte[35]);

        AssertUsable(result);
    }

    /* A span too short to hold a header does not throw and yields a usable placeholder format */
    [Fact]
    public void GetFormatFromWavHeader_ShortSpan_ReturnsUsableFormat()
    {
        AudioFormat result = FormatReader.GetFormatFromWavHeader(new byte[35].AsSpan());

        AssertUsable(result);
    }

    /* Both overloads agree on the same input */
    [Fact]
    public void GetFormatFromWavHeader_SameBytes_BothOverloadsAgree()
    {
        byte[] wav = WavWriter.CreateWav(new byte[4], new AudioFormat(32000, 2, 32));

        Assert.Equal(FormatReader.GetFormatFromWavHeader(wav), FormatReader.GetFormatFromWavHeader(wav.AsSpan()));
    }

    /* Checks that a format can be used to compute durations (no zero fields) */
    private static void AssertUsable(AudioFormat format)
    {
        Assert.True(format.SampleRate > 0);
        Assert.True(format.Channels > 0);
        Assert.True(format.BitDepth > 0);
    }
}
