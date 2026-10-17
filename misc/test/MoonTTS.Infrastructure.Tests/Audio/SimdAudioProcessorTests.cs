using System.Runtime.InteropServices;
using MoonTTS.Infrastructure.Audio;

namespace MoonTTS.Infrastructure.Tests.Audio;

/* SimdAudioProcessor scaling 16-bit PCM in place */
public sealed class SimdAudioProcessorTests
{
    /* 16-bit samples as their raw bytes */
    private static byte[] Pcm(params short[] samples) => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

    /* Raw bytes back as 16-bit samples */
    private static short[] Samples(byte[] pcm) => MemoryMarshal.Cast<byte, short>(pcm).ToArray();

    /* Scales a copy of the samples and returns the result */
    private static short[] Scale(float volume, params short[] samples)
    {
        byte[] pcm = Pcm(samples);
        SimdAudioProcessor.ScaleVolumeInPlace(pcm, volume);
        return Samples(pcm);
    }

    /* Half volume halves every sample */
    [Fact]
    public void ScaleVolumeInPlace_HalfVolume_HalvesSamples()
    {
        Assert.Equal([500, -500, 0, 16383], Scale(0.5f, 1000, -1000, 0, 32767));
    }

    /* Zero volume silences the buffer */
    [Fact]
    public void ScaleVolumeInPlace_ZeroVolume_Silences()
    {
        Assert.Equal([0, 0, 0], Scale(0f, 1234, -32768, 32767));
    }

    /* Unity volume (and values within 0.001 of it) leaves the buffer untouched */
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.0005f)]
    [InlineData(0.9995f)]
    public void ScaleVolumeInPlace_UnityVolume_LeavesSamplesUnchanged(float volume)
    {
        Assert.Equal([1001, -1001, 32767], Scale(volume, 1001, -1001, 32767));
    }

    /* Gain above one amplifies */
    [Fact]
    public void ScaleVolumeInPlace_Gain_AmplifiesSamples()
    {
        Assert.Equal([1500, -1500], Scale(1.5f, 1000, -1000));
    }

    /* Amplified samples are clipped to the 16-bit range instead of wrapping around */
    [Fact]
    public void ScaleVolumeInPlace_GainBeyondRange_ClipsToShortRange()
    {
        Assert.Equal([short.MaxValue, short.MinValue], Scale(2.0f, 30000, -30000));
    }

    /* Gains above 2 are clamped to 2 */
    [Fact]
    public void ScaleVolumeInPlace_VolumeAboveTwo_IsClampedToTwo()
    {
        Assert.Equal([200, -200], Scale(10f, 100, -100));
    }

    /* Negative volumes are clamped to 0 (silence), never inverting the signal */
    [Fact]
    public void ScaleVolumeInPlace_NegativeVolume_IsClampedToSilence()
    {
        Assert.Equal([0, 0], Scale(-1f, 100, -100));
    }

    /* An empty buffer is accepted */
    [Fact]
    public void ScaleVolumeInPlace_EmptyBuffer_DoesNothing()
    {
        Assert.Null(Record.Exception(() => SimdAudioProcessor.ScaleVolumeInPlace(Span<byte>.Empty, 0.5f)));
    }

    /* A trailing odd byte (half a sample) is left untouched */
    [Fact]
    public void ScaleVolumeInPlace_OddLength_LeavesTrailingByteUntouched()
    {
        byte[] pcm = [.. Pcm(1000), 0x7F];

        SimdAudioProcessor.ScaleVolumeInPlace(pcm, 0.5f);

        Assert.Equal(500, MemoryMarshal.Read<short>(pcm));
        Assert.Equal(0x7F, pcm[2]);
    }

    /* Only the given slice is scaled */
    [Fact]
    public void ScaleVolumeInPlace_Slice_ScalesOnlyThatSlice()
    {
        byte[] pcm = Pcm(1000, 1000);

        SimdAudioProcessor.ScaleVolumeInPlace(pcm.AsSpan(2), 0.5f);

        Assert.Equal([1000, 500], Samples(pcm));
    }
}
