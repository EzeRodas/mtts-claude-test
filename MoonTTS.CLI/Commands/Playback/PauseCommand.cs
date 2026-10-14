using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.CLI.commands;

/* Pauses the active speech playback session */

public static class PauseCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("pause", "Pauses audio playback.");
        command.SetAction((_) =>
        {
            var pipeline = services.GetRequiredService<ISpeechPipeline>();
            pipeline.Pause();
            Terminal.Warning("Playback paused.");
            return 0;
        });
        
        return command;
    }
}