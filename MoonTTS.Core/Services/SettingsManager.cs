using System.Collections.Concurrent;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Core.Services;

/* File Structure:
        - SettingsManager(IConfigurationRepository repo)
        - GetEngineConfig<T>(TtsModelType engine) : T
        - SaveAppConfig(AppConfig newConfig) : Result
        - SaveEngineConfig<T>(TtsModelType engine, T newConfig) : Result
        - GetModelsDirectory() : string
        - ReloadAppConfig() : void
*/

public class SettingsManager
{
    private readonly IConfigurationRepository _repo;
    
    /* Cache for current settings to avoid multiple reads on consecutive settings changes */
    public AppConfig CurrentAppConfig => _repo.AppConfig;
    
    /* Event for Avalonia to subscribe to refresh the UI */
    public event Action? SettingsChanged;

    /* Initializes settings manager and loads the current application configuration */
    public SettingsManager(IConfigurationRepository repo)
    {
        _repo = repo;
        _repo.AppConfigChanged += () => SettingsChanged?.Invoke();
        _repo.EngineConfigChanged += () => SettingsChanged?.Invoke();
    }

    /* Rwetrieves engine configuration */
    public T GetEngineConfig<T>(TtsModelType engine) where T : IEngineConfig,
        new() => _repo.GetEngineConfig<T>(engine);
    
    /* Saves global app configs and notifies the UI */
    public Result SaveAppConfig(AppConfig newConfig)
    {
        _repo.SaveAppConfig(newConfig);
        return Result.Success();
    }

    /* Saves engine specific configs and notifies the UI */
    public Result SaveEngineConfig<T>(TtsModelType engine, T newConfig) where T : IEngineConfig
    {
        _repo.SaveEngineConfig(engine, newConfig);
        return Result.Success();
    }

    /* Resolves the directory where TTS model files are stored */
    public string GetModelsDirectory()
    {
        /* If the user defined a custom path it gets returned and not overwritten */
        if (!string.IsNullOrWhiteSpace(CurrentAppConfig.CustomModelsDirectory))
        {
            return CurrentAppConfig.CustomModelsDirectory;
        }

        return PathResolver.GetModelsDirectory();
    }

    public void ReloadAppConfig()
    {
        _repo.LoadAll();
        SettingsChanged?.Invoke();
    }
    
}