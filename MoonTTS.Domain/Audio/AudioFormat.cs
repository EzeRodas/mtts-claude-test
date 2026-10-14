namespace MoonTTS.Domain.Audio;


/* Describes the pcm audio format (sample rate, channel count, bit depth) */
public readonly record struct AudioFormat(int SampleRate, short Channels, short BitDepth)
{
    public int BytesPerSample => BitDepth / 8;
    public int FrameSize => Channels * BytesPerSample;
    public int BytesPerSecond => SampleRate * FrameSize;

    /* Calculates playback duration from raw PCM byte count */
    public TimeSpan GetDuration(int pcmByteCount) =>
        TimeSpan.FromSeconds((double)pcmByteCount / BytesPerSecond);
}