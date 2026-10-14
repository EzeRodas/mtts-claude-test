using MoonTTS.Domain.Common;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Interfaces;

public interface IModelDownloader
{
    /* Downloads a model to the desired folder
        "model" is the model to download
        "destinationFolder" is the OS specific folder OR user selected directory where it'll be saved
        "progress" Reports back a value from 0.0 to 100.0 for the UI
        "cancellationToken" Allows to cancel the operation */
    Task<Result> DownloadModelAsync(
        ModelMetadata model,
        string destinationFolder,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}