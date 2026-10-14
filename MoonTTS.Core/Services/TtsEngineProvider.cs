using MoonTTS.Core.Helpers;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Core.Services;

public class TtsEngineProvider : ITtsEngineProvider
{
    private readonly ITtsEngineFactory _engineFactory;
    private readonly SettingsManager _settingsManager;
    private readonly DownloadManager? _downloadManager;
    private readonly IModelRegistry? _modelRegistry;
    private ITtsEngine? _currentEngine;

    public TtsModelType CurrentModelType { get; private set; } = TtsModelType.Unselected;

    public event Action? OnEngineChanged;

    /* Main constructor */
    public TtsEngineProvider(
        ITtsEngineFactory engineFactory,
        SettingsManager settingsManager,
        DownloadManager? downloadManager = null,
        IModelRegistry? modelRegistry = null
    )
    {
        _engineFactory = engineFactory;
        _settingsManager = settingsManager;
        _downloadManager = downloadManager;
        _modelRegistry = modelRegistry;
    }

    /* Lazily loads the engine specidied in AppConfig the first time it is accessed */
    public ITtsEngine CurrentEngine => _currentEngine ??= LoadEngineFromSettings();

    /* Creates and validates a candidate engine before replacing the active engine */
    public Result SetEngine(TtsModelType modelType)
    {
        ITtsEngine candidate;
        try
        {
            candidate = _engineFactory.Create(modelType);
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to load TTS engine '{modelType}': {ex.Message}", ex);
        }

        if (!candidate.IsAvailable && modelType != TtsModelType.Unselected)
        {
            candidate.Dispose();
            return Result.Failure(
                $"Model '{modelType}' was selected but files are not available or loaded."
            );
        }

        var previousEngine = _currentEngine;
        _currentEngine = candidate;
        CurrentModelType = modelType;
        previousEngine?.Dispose();
        OnEngineChanged?.Invoke();
        return Result.Success();
    }

    /* Validates and activates a model before saving it as the selected model */
    public Result SwitchModel(TtsModelType modelType)
    {
        if (modelType != TtsModelType.Unselected)
        {
            if (_modelRegistry != null && _modelRegistry.GetModelMetadata(modelType) == null)
                return Result.Failure($"Model '{modelType}' is not supported.");

            if (_downloadManager != null && !_downloadManager.IsModelDownloaded(modelType))
                return Result.Failure(
                    $"Cannot switch to '{modelType}'. Model files are not downloaded."
                );
        }

        var result = SetEngine(modelType);
        if (result.IsFailure)
            return result;

        var config = _settingsManager.CurrentAppConfig;
        config.ActiveModel = modelType;
        return _settingsManager.SaveAppConfig(config);
    }

    /* Reloads the active TTS engine based on current application configuration */
    public Result ReloadFromSettings() => SetEngine(_settingsManager.CurrentAppConfig.ActiveModel);

    /* Loads the active model configuration from settings and creates the corresponding engine */
    private ITtsEngine LoadEngineFromSettings()
    {
        var activeModel = _settingsManager.CurrentAppConfig.ActiveModel;
        CurrentModelType = activeModel;
        try
        {
            return _engineFactory.Create(activeModel);
        }
        catch
        {
            return new EmptyTtsEngine();
        }
    }
}
