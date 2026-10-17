using MoonTTS.Domain.Common;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Interfaces;

public interface ITtsEngineFactory
{
    /* Creates and fully initializes the engine for a model. Unselected yields an empty engine */
    Task<Result<ITtsEngine>> CreateAsync(ModelId modelId, CancellationToken cancellationToken = default);
}
