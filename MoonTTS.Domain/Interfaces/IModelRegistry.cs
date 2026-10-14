using MoonTTS.Domain.Enums;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Interfaces;

public interface IModelRegistry
{
    /* Returns a collection of all supported models by the app */
    IEnumerable<ModelMetadata> GetAvailableModels();

    /* Looks up the metadata for a specific model */
    ModelMetadata? GetModelMetadata(TtsModelType modelType);
}