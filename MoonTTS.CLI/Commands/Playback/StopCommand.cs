using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.CLI.commands;

/* Cancels synthesis and terminates active speech playback */

public static class StopCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("stop", "Stops audio playback.");
        command.SetAction((_) =>
        {
            var pipeline = services.GetRequiredService<ISpeechPipeline>();
            pipeline.Stop();
            Terminal.Warning("Playback stopped.");
            return 0;
        });
        
        return command;
    }
}