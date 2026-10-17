using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Core.Tests.Doubles;

/* File Structure:
        - CreateAsync(ModelId modelId, CancellationToken cancellationToken) : Task<Result<ITtsEngine>>
        - WaitForCallAsync(CancellationToken cancellationToken) : Task
        - Open() : void
*/

/* ITtsEngineFactory whose engine creation waits on a gate, measuring how many creations overlap */
public sealed class GatedTtsEngineFactory : ITtsEngineFactory
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _running;
    private int _callCount;
    private int _maxConcurrent;

    public int CallCount => Volatile.Read(ref _callCount);
    public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

    /* Waits for the gate, then returns a new available engine */
    public async Task<Result<ITtsEngine>> CreateAsync(ModelId modelId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        int running = Interlocked.Increment(ref _running);
        int seen;
        while (running > (seen = Volatile.Read(ref _maxConcurrent)))
            Interlocked.CompareExchange(ref _maxConcurrent, running, seen);

        _firstCall.TrySetResult();
        try
        {
            await _gate.Task.WaitAsync(cancellationToken);
            return Result<ITtsEngine>.Success(new FakeTtsEngine());
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    /* Completes once the first creation is waiting on the gate */
    public Task WaitForCallAsync(CancellationToken cancellationToken) =>
        _firstCall.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    /* Lets every waiting and future creation finish */
    public void Open() => _gate.TrySetResult();
}
