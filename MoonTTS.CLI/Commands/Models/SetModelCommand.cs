using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.CLI.commands;

/* Sets the current active model */

public static class SetModelCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("set", "Sets the active TTS model.");
        var modelArg = new Argument<string>("model") { Description = "The name of the model to activate." };
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
                Terminal.Muted("Available models: " + string.Join(", ", validModels));
                return 1;
            }

            /* Every installed engine's models are listed, but only downloaded ones can be activated */
            var downloadManager = services.GetRequiredService<DownloadManager>();
            if (!downloadManager.IsModelDownloaded(model.Id))
            {
                Terminal.Error($"Error: Model '{model.Id}' is not installed.");
                Terminal.Muted($"Download it first with: mtts model download {model.Id}");
                return 1;
            }

            var engineProvider = services.GetRequiredService<ITtsEngineProvider>();
            var result = await engineProvider.SwitchModelAsync(model.Id, cancellationToken);

            if (result.IsFailure)
            {
                Terminal.Error($"Failed: {result.ErrorMessage}");
                return 1;
            }
            
            Terminal.Success($"Successfully switched active model to: {model.Id}");
            return 0;
        });

        return command;
    }
}