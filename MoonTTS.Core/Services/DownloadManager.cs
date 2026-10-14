using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Core.Services;

/* File Structure:
        - DownloadManager(
            IModelRegistry modelRegistry,
            IModelDownloader modelDownloader,
            IConfigurationRepository repo)
        - GetAvailableModels() : IEnumerable<ModelMetadata>
        - IsModelDownloaded(TtsModelType modelType) : bool
        - DownloadModelAsync(
            TtsModelType modelType,
            IProgress<double>? progress,
            CancellationToken cancellationToken) : Task<Result>
        - DeleteModel(TtsModelType modelType) : Result
*/

public class DownloadManager
{
    private readonly IModelRegistry _modelRegistry;
    private readonly IModelDownloader _modelDownloader;
    private readonly SettingsManager _settingsManager;

    /* Gets the values through dependency injection */
    public DownloadManager(
        IModelRegistry modelRegistry,
        IModelDownloader modelDownloader,
        SettingsManager settingsManager
    )
    {
        _modelRegistry = modelRegistry;
        _modelDownloader = modelDownloader;
        _settingsManager = settingsManager;
    }

    /* Returns all supported models */
    public IEnumerable<ModelMetadata> GetAvailableModels()
    {
        return _modelRegistry.GetAvailableModels();
    }

    /* Checks if the file exists */
    public bool IsModelDownloaded(TtsModelType modelType)
    {
        var metadata = _modelRegistry.GetModelMetadata(modelType);
        if (metadata == null)
            return false;

        string modelsDir = _settingsManager.GetModelsDirectory();

        foreach (string expectedFileName in metadata.DownloadFiles.Keys)
        {
            string expectedPath = Path.Combine(modelsDir, expectedFileName);
            if (!File.Exists(expectedPath))
            {
                return false;
            }
        }
        return true;
    }

    /* Manages the entire download process */
    public async Task<Result> DownloadModelAsync(
        TtsModelType modelType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        /* Verifies the model is supported */
        var metadata = _modelRegistry.GetModelMetadata(modelType);
        if (metadata == null)
        {
            return Result.Failure(
                $"Model type '{modelType}' is not registered as a supported model."
            );
        }

        /* Gets the destination folder */
        string destinationFolder = _settingsManager.GetModelsDirectory();
        Directory.CreateDirectory(destinationFolder);

        /* Commands the infrastructure to perform the download */
        return await _modelDownloader.DownloadModelAsync(
            metadata,
            destinationFolder,
            progress,
            cancellationToken
        );
    }

    public Result DeleteModel(TtsModelType modelType)
    {
        /* Checks the model is registered */
        var metadata = _modelRegistry.GetModelMetadata(modelType);
        if (metadata == null)
            return Result.Failure($"Model '{modelType}' is not registered as a supported model.");

        string modelsDir = _settingsManager.GetModelsDirectory();
        Directory.CreateDirectory(modelsDir);

        bool anyDeleted = false;

        /* Checks that the file to be deleted is not required by any other model
           (Ex. many models might share the same voice file, so unless all of them are deleted, it'll keep that
           shared file) */
        try
        {
            foreach (string fileName in metadata.DownloadFiles.Keys)
            {
                string filePath = Path.Combine(modelsDir, fileName);
                if (!File.Exists(filePath))
                    continue;

                /* Filters to see if the other models share that key within their registry */
                bool isSharedWithDownloadedModel = _modelRegistry
                    .GetAvailableModels()
                    .Where(m => m.ModelType != modelType)
                    .Where(m => IsModelDownloaded(m.ModelType))
                    .Any(m => m.DownloadFiles.ContainsKey(fileName));

                if (!isSharedWithDownloadedModel)
                {
                    File.Delete(filePath);
                    anyDeleted = true;
                }
            }
            if (!anyDeleted)
                return Result.Failure($"No files found for model '{modelType}'.");

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed tod delete model files: {ex.Message}", ex);
        }
    }
}
