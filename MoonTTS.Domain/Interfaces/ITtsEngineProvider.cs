using MoonTTS.Domain.Common;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Interfaces;

public interface ITtsEngineProvider
{
    /* The loaded engine, or an empty engine while none has been loaded yet */
    ITtsEngine CurrentEngine { get; }
    ModelId CurrentModel { get; }
    event Action? OnEngineChanged;

    /* Returns the active engine, loading the model selected in settings the first time it is requested */
    Task<ITtsEngine> GetEngineAsync(CancellationToken cancellationToken = default);

    /* Sets the active TTS engine by model id */
    Task<Result> SetEngineAsync(ModelId modelId, CancellationToken cancellationToken = default);

    /* Switches the active TTS model and reinitializes the engine */
    Task<Result> SwitchModelAsync(ModelId modelId, CancellationToken cancellationToken = default);

    /* Reloads the active TTS engine based on current application settings */
    Task<Result> ReloadFromSettingsAsync(CancellationToken cancellationToken = default);
}
