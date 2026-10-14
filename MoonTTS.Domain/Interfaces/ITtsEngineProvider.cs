using MoonTTS.Domain.Common;
using MoonTTS.Domain.Enums;

namespace MoonTTS.Domain.Interfaces;

public interface ITtsEngineProvider
{
    ITtsEngine CurrentEngine { get; }
    TtsModelType CurrentModelType { get; }
    event Action? OnEngineChanged;
    /* Sets the active TTS engine by model type */
    Result SetEngine(TtsModelType engine);

    /* Switches the active TTS model and reinitializes the engine */
    Result SwitchModel(TtsModelType modelType);

    /* Reloads the active TTS engine based on current application settings */
    Result ReloadFromSettings();
}