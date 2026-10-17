using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.commands;

/* Deletes downloaded model files from disk with confirmation */

public static class DeleteModelCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("delete", "Deletes the selected model files.");
        var modelArg = new Argument<string>("model") { Description = "The name of the model to delete." };
        var skipOpt = new Option<bool>("-s", "--skip") { Description = "Skip confirmation prompt." };

        command.Add(modelArg);
        command.Add(skipOpt);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string modelInput = parseResult.GetValue(modelArg) ?? string.Empty;
            bool skipConfirm = parseResult.GetValue(skipOpt);

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
            var settingsManager = services.GetRequiredService<SettingsManager>();
            var engineProvider = services.GetRequiredService<ITtsEngineProvider>();

            if (!downloadManager.IsModelDownloaded(modelId))
            {
                Terminal.Warning($"Model '{modelId}' is not downloaded.");
                return 0;
            }

            bool isActive = settingsManager.CurrentAppConfig.ActiveModel == modelId;
            if (isActive)
            {
                Terminal.Warning($"Warning: '{modelId}' is currently the active model.");
                if (!skipConfirm && !Terminal.Confirm("Deleting it will set active model to Unselected. Proceed?"))
                {
                    Terminal.Muted("Deletion cancelled.");
                    return 0;
                }

                await engineProvider.SwitchModelAsync(ModelId.Unselected, cancellationToken);
            }
            else if (!skipConfirm && !Terminal.Confirm($"Are you sure you want to delete model '{modelId}'?"))
            {
                Terminal.Muted("Deletion cancelled.");
                return 0;
            }

            var deleteResult = downloadManager.DeleteModel(modelId);
            if (deleteResult.IsFailure)
            {
                Terminal.Error($"Failed to delete model files: {deleteResult.ErrorMessage}");
                return 1;
            }

            Terminal.Success($"Successfully deleted model: {modelId}");
            return 0;
        });

        return command;
    }
}