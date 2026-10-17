using MoonTTS.Core.Helpers;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.Tts;

/* Provides the valid engines to the service registry */
/* File Structure:
        - TtsEngineFactory(
            IModelRegistry modelRegistry,
            SettingsManager settingsManager,
            DownloadManager downloadManager,
            EngineVoiceStore voiceStore)
        - CreateAsync(ModelId modelId, CancellationToken cancellationToken) : Task<Result<ITtsEngine>>
*/

public class TtsEngineFactory : ITtsEngineFactory
{
    private readonly IModelRegistry _modelRegistry;
    private readonly SettingsManager _settingsManager;
    private readonly DownloadManager _downloadManager;
    private readonly EngineVoiceStore _voiceStore;

    /* Gets the values through dependency injection */
    public TtsEngineFactory(
        IModelRegistry modelRegistry,
        SettingsManager settingsManager,
        DownloadManager downloadManager,
        EngineVoiceStore voiceStore)
    {
        _modelRegistry = modelRegistry;
        _settingsManager = settingsManager;
        _downloadManager = downloadManager;
        _voiceStore = voiceStore;
    }

    /* Every installed engine is served by the same out-of-process adapter, so adding an engine needs no code here */
    public async Task<Result<ITtsEngine>> CreateAsync(ModelId modelId, CancellationToken cancellationToken = default)
    {
        if (modelId.IsUnselected)
            return Result<ITtsEngine>.Success(new EmptyTtsEngine());

        var model = _modelRegistry.GetModelMetadata(modelId);
        if (model == null)
            return Result<ITtsEngine>.Failure($"Model '{modelId}' is not provided by any installed engine.");

        var engine = _modelRegistry.GetEngine(modelId.EngineId);
        if (engine == null)
            return Result<ITtsEngine>.Failure($"Engine '{modelId.EngineId}' is not installed.");

        if (!_downloadManager.IsModelDownloaded(modelId))
            return Result<ITtsEngine>.Failure($"Model '{modelId}' files are not downloaded.");

        return await ExternalTtsEngine.CreateAsync(engine, model, _settingsManager, _voiceStore, cancellationToken)
            .ConfigureAwait(false);
    }
}
