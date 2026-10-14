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

    /* Smooths sample boundaries (3ms fade-in, 5ms fade-out) in-place to prevent speaker clicking */
    public static void ApplyDeClickingInPlace(Span<byte> pcmBytes, int sampleRate = 24000)
    {
        if (pcmBytes.Length < 4) return;
        Span<short> samples = MemoryMarshal.Cast<byte, short>(pcmBytes);

        int fadeInSamples = Math.Min(sampleRate * 3 / 1000, samples.Length / 2);
        for (int i = 0; i < fadeInSamples; i++)
        {
            float factor = (float)i / fadeInSamples;
            samples[i] = (short)(samples[i] * factor);
        }

        int fadeOutSamples = Math.Min(sampleRate * 5 / 1000, samples.Length / 2);
        for (int i = 0; i < fadeOutSamples; i++)
        {
            float factor = (float)i / fadeOutSamples;
            int idx = samples.Length - 1 - i;
            samples[idx] = (short)(samples[idx] * factor);
        }
    }
}