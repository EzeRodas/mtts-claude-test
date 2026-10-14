using Microsoft.Extensions.DependencyInjection;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Core.Helpers;
using MoonTTS.Core.Services;

namespace MoonTTS.Infrastructure.Tts;

/* Provides the valid engines to the service registry */
/* File Structure:
        - TtsEngineFactory(IServiceProvider services)
        - Create(TtsModelType modelType) : ITtsEngine
*/

public class TtsEngineFactory : ITtsEngineFactory
{
    private readonly IServiceProvider _services;
    
    public TtsEngineFactory(IServiceProvider services)
    {
        _services = services;
    }

    /* Here is where you filter which class to use depending on model type */
    /* If you want to add a model you need to filter its types here */
    public ITtsEngine Create(TtsModelType modelType) => modelType switch
    {
        /* We use GetRequiredService to make sure it won't be null at runtime, if it is, it'll throw an error */
        TtsModelType.Kokoro or
        TtsModelType.Kokoro_fp16 or
        TtsModelType.Kokoro_zh_fp16 =>
            new KokoroOnnxEngine(_services.GetRequiredService<SettingsManager>(), modelType),
        _ => new EmptyTtsEngine()
    };
}