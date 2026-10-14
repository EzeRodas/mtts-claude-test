using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;

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
    
    /* Returns the list of voice identifiers supported by this engine */
    List<string> ListVoices();
}