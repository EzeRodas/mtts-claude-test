using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Domain.Enums;
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

        command.SetAction((parseResult) =>
        {
            string modelInput = parseResult.GetValue(modelArg) ?? string.Empty;
            if (!Enum.TryParse<TtsModelType>(modelInput, true, out var modelType))
            {
                Terminal.Error($"Error: Unknown model '{modelInput}'.");
                Terminal.Muted("Available models: " + string.Join(", ", Enum.GetNames<TtsModelType>()));
                return 1;
            }

            var engineProvider = services.GetRequiredService<ITtsEngineProvider>();
            var result = engineProvider.SwitchModel(modelType);

            if (result.IsFailure)
            {
                Terminal.Error($"Failed: {result.ErrorMessage}");
                return 1;
            }
            
            Terminal.Success($"Successfully switched active model to: {modelType}");
            return 0;
        });

        return command;
    }
}