using System.Runtime.InteropServices;

namespace MoonTTS.Infrastructure.Audio;

/* Audio digital signal processing routines operating directly on raw PCM memory spans */
public static class SimdAudioProcessor
{
    /* Scales 16-bit PCM samples by a volume factor in-place with zero allocations */
    public static void ScaleVolumeInPlace(Span<byte> pcmBytes, float volume)
    {
        if (pcmBytes.IsEmpty || Math.Abs(volume - 1.0f) < 0.001f)
            return;

        float clampedVol = Math.Clamp(volume, 0f, 2.0f);
        Span<short> samples = MemoryMarshal.Cast<byte, short>(pcmBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)Math.Clamp((int)(samples[i] * clampedVol), short.MinValue, short.MaxValue);
        }
    }
}