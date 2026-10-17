using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.CLI.commands;

/* Sets the active voice for the active TTS engine */

public static class SetVoiceCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("set", "Sets the active voice.");
        var voiceArg = new Argument<string>("voice") { Description = "The ID of the voice to activate." };
        command.Add(voiceArg);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string voice = parseResult.GetValue(voiceArg) ?? string.Empty;
            var engineProvider = services.GetRequiredService<ITtsEngineProvider>();

            var engine = await engineProvider.GetEngineAsync(cancellationToken);
            if (!engine.IsAvailable)
            {
                Terminal.Error("Error: No TTS model is currently loaded.");
                return 1;
            }

            var availableVoices = engine.ListVoices();
            var targetVoice = availableVoices
                .FirstOrDefault(v => string.Equals(v.Id, voice, StringComparison.OrdinalIgnoreCase))?.Id;

            if (targetVoice == null)
            {
                Terminal.Error($"Error: Unknown voice '{voice}'.");
                Terminal.Muted("Run 'mtts voice list' to view available voices.");
                return 1;
            }

            try
            {
                engine.ActiveVoice = targetVoice;
                Terminal.Success($"Successfully switched active voice to: {targetVoice}");
                return 0;
            }
            catch (Exception ex)
            {
                Terminal.Error($"Failed to set voice: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}