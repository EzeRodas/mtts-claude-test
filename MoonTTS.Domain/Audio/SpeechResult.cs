namespace MoonTTS.Domain.Audio;

/* Performance and outcome metadata returned by the speech pipeline */
public sealed class SpeechResult
{
    /* Total duration of synthesized audio */
    public TimeSpan AudioDuration { get; init; }

    /* Latency elapsed between synthesis request and first audible audio chunk */
    public TimeSpan TimeToFirstSound { get; init; }

    /* Total raw PCM audio data in bytes */
    public int TotalPcmBytes { get; init; }
}