using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;

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

        command.SetAction((parseResult) =>
        {
            string modelInput = parseResult.GetValue(modelArg) ?? string.Empty;
            bool skipConfirm = parseResult.GetValue(skipOpt);

            if (!Enum.TryParse<TtsModelType>(modelInput, true, out var modelType) ||
                modelType == TtsModelType.Unselected)
            {
                Terminal.Error($"Error: Unknown model '{modelInput}'.");
                var validModels = Enum.GetNames<TtsModelType>().Where(m => m != nameof(TtsModelType.Unselected));
                Terminal.Muted("Supported models: " + string.Join(", ", validModels));
                return 1;
            }

            var downloadManager = services.GetRequiredService<DownloadManager>();
            var settingsManager = services.GetRequiredService<SettingsManager>();
            var engineProvider = services.GetRequiredService<ITtsEngineProvider>();

            if (!downloadManager.IsModelDownloaded(modelType))
            {
                Terminal.Warning($"Model '{modelType}' is not downloaded.");
                return 0;
            }

            bool isActive = settingsManager.CurrentAppConfig.ActiveModel == modelType;
            if (isActive)
            {
                Terminal.Warning($"Warning: '{modelType}' is currently the active model.");
                if (!skipConfirm && !Terminal.Confirm("Deleting it will set active model to Unselected. Proceed?"))
                {
                    Terminal.Muted("Deletion cancelled.");
                    return 0;
                }

                engineProvider.SwitchModel(TtsModelType.Unselected);
            }
            else if (!skipConfirm && !Terminal.Confirm($"Are you sure you want to delete model '{modelType}'?"))
            {
                Terminal.Muted("Deletion cancelled.");
                return 0;
            }

            var deleteResult = downloadManager.DeleteModel(modelType);
            if (deleteResult.IsFailure)
            {
                Terminal.Error($"Failed to delete model files: {deleteResult.ErrorMessage}");
                return 1;
            }

            Terminal.Success($"Successfully deleted model: {modelType}");
            return 0;
        });

        return command;
    }
}