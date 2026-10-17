using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Core.Tests.Doubles;

/* File Structure:
        - SynthesizeStreamAsync(string text, float? speedOverride, CancellationToken cancellationToken) : IAsyncEnumerable<ReadOnlyMemory<byte>>
        - SynthesizeAsync(string text, float? speedOverride, CancellationToken cancellationToken) : Task<Result<byte[]>>
        - WaitForStreamStartAsync(CancellationToken cancellationToken) : Task<int>
        - WaitForStreamCloseAsync(CancellationToken cancellationToken) : Task<int>
        - ListVoices() : IReadOnlyList<VoiceInfo>
        - Dispose() : void
*/

/* ITtsEngine whose streams can hang until cancelled and that signals when each stream starts and closes,
   so tests can await the pipeline's state instead of sleeping */
public sealed class ControllableTtsEngine : ITtsEngine
{
    private readonly Channel<int> _started = Channel.CreateUnbounded<int>();
    private readonly Channel<int> _closed = Channel.CreateUnbounded<int>();
    private int _requestCount;

    public AudioFormat OutputFormat { get; set; } = new(16000, 1, 16);
    public bool IsAvailable { get; set; } = true;
    public float Speed { get; set; } = 1.0f;
    public string ActiveVoice { get; set; } = string.Empty;

    /* Chunks yielded per request before the stream either ends or hangs */
    public int ChunksPerRequest { get; set; } = 1;
    public int ChunkSize { get; set; } = 10;

    /* When true the stream waits for cancellation after yielding its chunks */
    public bool HangAfterChunks { get; set; } = true;

    /* Yields the scripted chunks, signalling the start and the close (completion, cancellation or disposal) of
       every stream with the request's zero-based index */
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text,
        float? speedOverride = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int index = Interlocked.Increment(ref _requestCount) - 1;
        try
        {
            _started.Writer.TryWrite(index);
            for (int i = 0; i < ChunksPerRequest; i++)
            {
                await Task.Yield();
                yield return new byte[ChunkSize];
            }

            if (HangAfterChunks)
                await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            _closed.Writer.TryWrite(index);
        }
    }

    /* Not used by the pipeline; concatenates the scripted stream */
    public async Task<Result<byte[]>> SynthesizeAsync(string text, float? speedOverride = default, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await foreach (var chunk in SynthesizeStreamAsync(text, speedOverride, cancellationToken))
            ms.Write(chunk.Span);
        return Result<byte[]>.Success(ms.ToArray());
    }

    /* Waits until the next stream starts and returns its request index */
    public async Task<int> WaitForStreamStartAsync(CancellationToken cancellationToken) =>
        await _started.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    /* Waits until the next stream closes and returns its request index */
    public async Task<int> WaitForStreamCloseAsync(CancellationToken cancellationToken) =>
        await _closed.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    /* No voices */
    public IReadOnlyList<VoiceInfo> ListVoices() => Array.Empty<VoiceInfo>();

    /* Nothing to release */
    public void Dispose() { }
}
