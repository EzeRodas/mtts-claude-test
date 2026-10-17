using MoonTTS.Domain.Audio;

namespace MoonTTS.Domain.Tests.Audio;

/* Verifies the PCM size calculations derived from an AudioFormat */
public sealed class AudioFormatTests
{
    /* The constructor stores sample rate, channel count and bit depth */
    [Fact]
    public void Constructor_Always_StoresComponents()
    {
        var format = new AudioFormat(24000, 1, 16);

        Assert.Equal(24000, format.SampleRate);
        Assert.Equal((short)1, format.Channels);
        Assert.Equal((short)16, format.BitDepth);
    }

    /* BytesPerSample is the bit depth expressed in whole bytes */
    [Theory]
    [InlineData(8, 1)]
    [InlineData(16, 2)]
    [InlineData(24, 3)]
    [InlineData(32, 4)]
    public void BytesPerSample_ForBitDepth_IsBitDepthDividedByEight(short bitDepth, int expected)
    {
        var format = new AudioFormat(48000, 1, bitDepth);

        Assert.Equal(expected, format.BytesPerSample);
    }

    /* FrameSize is one sample for every channel */
    [Theory]
    [InlineData(1, 16, 2)]
    [InlineData(2, 16, 4)]
    [InlineData(2, 24, 6)]
    [InlineData(6, 32, 24)]
    public void FrameSize_ForChannelsAndBitDepth_IsChannelsTimesBytesPerSample(short channels, short bitDepth, int expected)
    {
        var format = new AudioFormat(48000, channels, bitDepth);

        Assert.Equal(expected, format.FrameSize);
    }

    /* BytesPerSecond is one frame per sample period */
    [Theory]
    [InlineData(24000, 1, 16, 48000)]
    [InlineData(22050, 1, 8, 22050)]
    [InlineData(44100, 2, 16, 176400)]
    [InlineData(48000, 2, 24, 288000)]
    public void BytesPerSecond_ForFormat_IsSampleRateTimesFrameSize(int sampleRate, short channels, short bitDepth, int expected)
    {
        var format = new AudioFormat(sampleRate, channels, bitDepth);

        Assert.Equal(expected, format.BytesPerSecond);
    }

    /* GetDuration converts a byte count into playback time */
    [Theory]
    [InlineData(48000, 1000)]
    [InlineData(24000, 500)]
    [InlineData(96000, 2000)]
    [InlineData(4800, 100)]
    public void GetDuration_ForByteCount_ReturnsPlaybackTime(int pcmByteCount, int expectedMilliseconds)
    {
        var format = new AudioFormat(24000, 1, 16);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), format.GetDuration(pcmByteCount));
    }

    /* GetDuration of no bytes is zero */
    [Fact]
    public void GetDuration_ZeroBytes_ReturnsZero()
    {
        var format = new AudioFormat(44100, 2, 16);

        Assert.Equal(TimeSpan.Zero, format.GetDuration(0));
    }

    /* Two formats with the same components are equal (value semantics) */
    [Fact]
    public void Equality_SameComponents_AreEqual()
    {
        Assert.Equal(new AudioFormat(24000, 1, 16), new AudioFormat(24000, 1, 16));
    }

    /* Formats that differ in any component are not equal */
    [Theory]
    [InlineData(22050, 1, 16)]
    [InlineData(24000, 2, 16)]
    [InlineData(24000, 1, 24)]
    public void Equality_DifferentComponent_AreNotEqual(int sampleRate, short channels, short bitDepth)
    {
        Assert.NotEqual(new AudioFormat(24000, 1, 16), new AudioFormat(sampleRate, channels, bitDepth));
    }
}
