namespace MoonTTS.Core.Helpers;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;

public class EmptyTtsEngine : ITtsEngine
{
    public AudioFormat OutputFormat => new AudioFormat(24000, 1, 16);
    public bool IsAvailable => false;
    public float Speed { get; set; } = 1.0f;
    public string ActiveVoice { get; set; } =  string.Empty;

    /* Returns an empty list since no engine is active */
    public List<string> ListVoices() => new();

    /* Yields no audio chunks since no engine is active */
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text, 
        float? speedOverride = default, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /* Returns a failure result notifying the caller that no engine is selected */
    public Task<Result<byte[]>> SynthesizeAsync(
        string text, 
        float? speedOverride = default, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<byte[]>.Failure("No TTS engine is selected. Please select a model before synthesizing."));
    }
    
    /* Disposes resources (no-op for empty engine) */
    public void Dispose() { }
}