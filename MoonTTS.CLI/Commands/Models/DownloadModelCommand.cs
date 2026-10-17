using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

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

            var modelRegistry = services.GetRequiredService<IModelRegistry>();
            var model = modelRegistry.FindModel(modelInput);
            if (model == null)
            {
                Terminal.Error($"Error: Unknown model '{modelInput}'.");
                var validModels = modelRegistry.GetAvailableModels().Select(m => m.Id.ToString());
                Terminal.Muted("Supported models: " + string.Join(", ", validModels));
                return 1;
            }
            var modelId = model.Id;

            var downloadManager = services.GetRequiredService<DownloadManager>();

            if (downloadManager.IsModelDownloaded(modelId))
            {
                Terminal.Warning($"Model '{modelId}' is already downloaded.");
                return 0;
            }

            Terminal.Info($"Downloading model files for '{modelId}'...");

            var progress = new Progress<double>(percent =>
            {
                Terminal.RenderProgressBar($"Downloading {modelId}", (int)(percent * 100));
            });

            var result = await downloadManager.DownloadModelAsync(modelId, progress, cancellationToken);
            if (result.IsFailure)
            {
                Terminal.Error($"\nDownload failed: {result.ErrorMessage}");
                return 1;
            }

            Terminal.Success($"\nSuccessfully downloaded model: {modelId}");
            return 0;
        });

        return command;
    }
}