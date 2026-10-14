using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Infrastructure.Daemon;

namespace MoonTTS.CLI.commands.Daemon;

/* Starts the background daemon and loads the model */

public static class DaemonStartCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("start", "Starts the background daemon and pre-loads the model.");
        command.SetAction(async (_, cancellationToken) =>
        {
            var client = services.GetRequiredService<DaemonClient>();

            if (await client.IsRunningAsync())
            {
                Terminal.Warning("Moon-TTS daemon is already running.");
                return 0;
            }

            Terminal.Info("Starting daemon and loading model...");
            var res = await client.EnsureRunningAsync();
            if (res.IsFailure)
            {
                Terminal.Error($"Error: {res.ErrorMessage}");
                return 1;
            }

            if (await client.IsRunningAsync())
            {
                Terminal.Success("Daemon started successfully. Model is resident in memory.");
                return 0;
            }
            
            return 1;
        });
        return command;
    }
}