using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Core.Tests.Doubles;

/* File Structure:
        - StubTtsEngineProvider(ITtsEngine engine)
        - GetEngineAsync(CancellationToken cancellationToken) : Task<ITtsEngine>
        - SetEngineAsync(ModelId modelId, CancellationToken cancellationToken) : Task<Result>
        - SwitchModelAsync(ModelId modelId, CancellationToken cancellationToken) : Task<Result>
        - ReloadFromSettingsAsync(CancellationToken cancellationToken) : Task<Result>
*/

/* ITtsEngineProvider that always hands out the same engine, isolating SpeechPipeline from engine loading */
public sealed class StubTtsEngineProvider : ITtsEngineProvider
{
    private readonly ITtsEngine _engine;

    /* Wraps the engine every request receives */
    public StubTtsEngineProvider(ITtsEngine engine)
    {
        _engine = engine;
    }

    public ITtsEngine CurrentEngine => _engine;
    public ModelId CurrentModel => ModelId.Unselected;

    /* Never raised: the engine never changes */
    public event Action? OnEngineChanged { add { } remove { } }

    /* Returns the wrapped engine */
    public Task<ITtsEngine> GetEngineAsync(CancellationToken cancellationToken = default) => Task.FromResult(_engine);

    /* Accepts any model without changing the engine */
    public Task<Result> SetEngineAsync(ModelId modelId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /* Accepts any model without changing the engine */
    public Task<Result> SwitchModelAsync(ModelId modelId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    /* Nothing to reload */
    public Task<Result> ReloadFromSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
