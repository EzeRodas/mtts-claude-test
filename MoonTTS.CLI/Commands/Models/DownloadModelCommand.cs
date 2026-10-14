using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Enums;

namespace MoonTTS.CLI.commands;

/* Downloads model files from HuggingFace with terminal progress */

public static class DownloadModelCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("download", "Downloads the selected model files.");
        var modelArg = new Argument<string>("model") { Description = "The name of the model to download." };
        command.Add(modelArg);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string modelInput = parseResult.GetValue(modelArg) ?? string.Empty;

            if (!Enum.TryParse<TtsModelType>(modelInput, true, out var modelType) ||
                modelType == TtsModelType.Unselected)
            {
                Terminal.Error($"Error: Unknown model '{modelInput}'.");
                var validModels = Enum.GetNames<TtsModelType>().Where(m => m != nameof(TtsModelType.Unselected));
                Terminal.Muted("Supported models: " + string.Join(", ", validModels));
                return 1;
            }

            var downloadManager = services.GetRequiredService<DownloadManager>();

            if (downloadManager.IsModelDownloaded(modelType))
            {
                Terminal.Warning($"Model '{modelType}' is already downloaded.");
                return 0;
            }

            Terminal.Info($"Downloading model files for '{modelType}'...");

            var progress = new Progress<double>(percent =>
            {
                Terminal.RenderProgressBar($"Downloading {modelType}", (int)(percent * 100));
            });

            var result = await downloadManager.DownloadModelAsync(modelType, progress, cancellationToken);
            if (result.IsFailure)
            {
                Terminal.Error($"\nDownload failed: {result.ErrorMessage}");
                return 1;
            }

            Terminal.Success($"\nSuccessfully downloaded model: {modelType}");
            return 0;
        });

        return command;
    }
}