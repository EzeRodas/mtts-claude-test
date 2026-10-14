using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Infrastructure.Daemon;

namespace MoonTTS.CLI.commands.Daemon;

/* Stops the running background daemon */

public static class DaemonKillCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("kill", "Stops the running background daemon.");
        command.SetAction(async (_, cancellationToken) =>
        {
            var client = services.GetRequiredService<DaemonClient>();
            var result = await client.KillDaemonAsync(cancellationToken);
            if (result.IsSuccess)
            {
                Terminal.Success("Daemon stopped successfully.");
                return 0;
            }

            Terminal.Warning(result.ErrorMessage ?? "Failed to stop daemon.");
            return 1;
        });
        return command;
    }
}