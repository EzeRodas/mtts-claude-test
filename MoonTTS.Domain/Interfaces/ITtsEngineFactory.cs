using MoonTTS.Domain.Enums;

namespace MoonTTS.Domain.Interfaces;

public interface ITtsEngineFactory
{
    ITtsEngine Create(TtsModelType modelType);
}