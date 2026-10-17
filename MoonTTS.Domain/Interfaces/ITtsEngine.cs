using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Interfaces;

public interface ITtsEngine : IDisposable
{
    AudioFormat OutputFormat { get; }
    bool IsAvailable { get; }
    float Speed { get; set; }
    string ActiveVoice { get; set; }

    /* Synthesizes input text into a stream of raw PCM audio chunks */
    IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text,
        float? speedOverride = default,
        CancellationToken cancellationToken = default);

    /* Synthesizes input text into a complete RIFF WAV byte array */
    Task<Result<byte[]>> SynthesizeAsync(
        string text,
        float? speedOverride = default,
        CancellationToken cancellationToken = default);

    /* Returns the voices supported by the loaded model */
    IReadOnlyList<VoiceInfo> ListVoices();
}
