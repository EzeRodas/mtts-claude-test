using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Infrastructure.Daemon;

namespace MoonTTS.CLI.commands.Daemon;

/* Retrieves status and remaining idle time of the background daemon */

public static class DaemonStatusCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("status", "Retrieves the status and remaining time of the background daemon.");
        command.SetAction(async (_, cancellationToken) =>
        {
            var client = services.GetRequiredService<DaemonClient>();
            var status = await client.GetStatusAsync(cancellationToken);

            if (status == null)
            {
                Terminal.Warning("Daemon is currently: STOPPED");
                return 0;
            }

            int min = status.IdleSecondsRemaining / 60;
            int sec = status.IdleSecondsRemaining % 60;
            
            var headers = new[] { "Status", "Active Model", "Shutdown In" };
            
            var rows = new[]
            {
                new[] { "RUNNING", status.ActiveModel ?? "-", $"{min}m {sec}s" }
            };

            Terminal.Table("Daemon Process Status", headers, rows);
            return 0;
        });
        return command;
    }
}