using System.Runtime.CompilerServices;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - SynthesizeStreamAsync(string text, float? speedOverride, CancellationToken cancellationToken) : IAsyncEnumerable<ReadOnlyMemory<byte>>
        - SynthesizeAsync(string text, float? speedOverride, CancellationToken cancellationToken) : Task<Result<byte[]>>
        - ListVoices() : IReadOnlyList<VoiceInfo>
        - Dispose() : void
*/

/* Scriptable ITtsEngine: yields ChunksPerRequest chunks of ChunkSize bytes per call and records every request */
public sealed class FakeTtsEngine : ITtsEngine
{
    public AudioFormat OutputFormat { get; set; } = new(22050, 1, 16);
    public bool IsAvailable { get; set; } = true;
    public float Speed { get; set; } = 1.0f;
    public string ActiveVoice { get; set; } = string.Empty;

    public List<VoiceInfo> Voices { get; } = new();
    public int ChunksPerRequest { get; set; } = 2;
    public int ChunkSize { get; set; } = 100;

    /* When set, synthesis throws this exception after yielding nothing */
    public Exception? ThrowOnSynthesize { get; set; }

    public List<(string Text, float? Speed)> Requests { get; } = new();
    public bool IsDisposed { get; private set; }

    /* Yields the scripted chunks; byte values are the chunk index so tests can check ordering */
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text,
        float? speedOverride = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add((text, speedOverride));
        if (ThrowOnSynthesize != null)
            throw ThrowOnSynthesize;

        for (int i = 0; i < ChunksPerRequest; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return Enumerable.Repeat((byte)i, ChunkSize).ToArray();
        }
    }

    /* Concatenates the scripted chunks (no WAV header) */
    public async Task<Result<byte[]>> SynthesizeAsync(string text, float? speedOverride = default, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await foreach (var chunk in SynthesizeStreamAsync(text, speedOverride, cancellationToken))
            ms.Write(chunk.Span);
        return Result<byte[]>.Success(ms.ToArray());
    }

    /* Returns the configured voices */
    public IReadOnlyList<VoiceInfo> ListVoices() => Voices;

    /* Marks the engine as disposed */
    public void Dispose() => IsDisposed = true;
}

/* File Structure:
        - CreateAsync(ModelId modelId, CancellationToken cancellationToken) : Task<Result<ITtsEngine>>
*/

/* ITtsEngineFactory returning preconfigured engines (or failures) per model */
public sealed class FakeTtsEngineFactory : ITtsEngineFactory
{
    public Dictionary<ModelId, Func<ITtsEngine>> Engines { get; } = new();
    public Dictionary<ModelId, string> Failures { get; } = new();
    public List<ModelId> CreatedModels { get; } = new();

    /* Builds the engine registered for the model, an empty-like unavailable engine for Unselected, or a failure */
    public Task<Result<ITtsEngine>> CreateAsync(ModelId modelId, CancellationToken cancellationToken = default)
    {
        CreatedModels.Add(modelId);
        if (Failures.TryGetValue(modelId, out var error))
            return Task.FromResult(Result<ITtsEngine>.Failure(error));
        if (Engines.TryGetValue(modelId, out var build))
            return Task.FromResult(Result<ITtsEngine>.Success(build()));
        return Task.FromResult(Result<ITtsEngine>.Success(new FakeTtsEngine { IsAvailable = false }));
    }
}
