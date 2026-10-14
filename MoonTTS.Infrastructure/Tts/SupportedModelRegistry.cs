using System.Text.Json;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.Tts;

/* Acts as a container for all supported models */
/* Needs to be updated when adding a model */
public class SupportedModelRegistry : IModelRegistry
{
    private readonly List<ModelMetadata> _supportedModels = new()
    {
        ModelLibrary.KokoroFp32,
        ModelLibrary.KokoroFp16,
        ModelLibrary.KokoroZhFp16
    };

    public IEnumerable<ModelMetadata> GetAvailableModels() => _supportedModels;

    public ModelMetadata? GetModelMetadata(TtsModelType modelType) =>  
        _supportedModels.FirstOrDefault(m => m.ModelType == modelType);
}