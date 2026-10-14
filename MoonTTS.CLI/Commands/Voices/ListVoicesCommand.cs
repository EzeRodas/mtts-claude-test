using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.CLI.commands;

/* Lists available voices for the active model */

public static class ListVoicesCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("list", "Lists available voices for the active model.");
        var filterArg = new Argument<string?>("filter") { Description = "Optional search filter by voice name.",
            Arity = ArgumentArity.ZeroOrOne };
        command.Add(filterArg);

        command.SetAction((parseResult) =>
        {
            var engineProvider = services.GetRequiredService<ITtsEngineProvider>();
            var settingsManager = services.GetRequiredService<SettingsManager>();

            var engine = engineProvider.CurrentEngine;
            if (!engine.IsAvailable)
            {
                Terminal.Error("Error: No TTS model is currently loaded.");
                Terminal.Muted(
                    "Select a model first with: mtts model set <MODEL>, or see the ones available with: mtts model list");
                return 1;
            }

            var allVoices = engine.ListVoices();
            if (allVoices.Count == 0)
            {
                Terminal.Warning("No voices available for the active model.");
                return 0;
            }
            
            string? filter = parseResult.GetValue(filterArg);
            var displayVoices = string.IsNullOrWhiteSpace(filter)
                ? allVoices
                : allVoices.Where(v => v.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (displayVoices.Count == 0)
            {
                Terminal.Warning($"No voices matched filter '{filter}'.");
                return 0;
            }

            string activeVoice = engine.ActiveVoice;

            var headers = new[] { "Voice ID", "Status" };
            var rows = displayVoices.Select(v => new[]
            {
                v,
                string.Equals(v, activeVoice, StringComparison.OrdinalIgnoreCase) ? "ACTIVE" : "-"
            });
            
            var activeModel = settingsManager.CurrentAppConfig.ActiveModel;
            Terminal.Table($"Available Voices for '{activeModel}'", headers, rows);
            Terminal.Muted($"Showing {displayVoices.Count} of {allVoices.Count} voices. Switch with: mtts voice set <VOICE>");
            return 0;
        });

        return command;
    }
}