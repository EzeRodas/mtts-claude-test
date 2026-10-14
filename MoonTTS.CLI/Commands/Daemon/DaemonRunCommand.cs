using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.Infrastructure.Daemon;

namespace MoonTTS.CLI.commands.Daemon;

/* File Structure:
        - DaemonRunCommand
            - Build(IServiceProvider services) : Command
*/

public static class DaemonRunCommand
{
    /* Internal background daemon loop executed by auto-spawn */
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("run", "Internal daemon execution loop.") { Hidden = true };
        command.SetAction(async (_, cancellationToken) =>
        {
            var server = services.GetRequiredService<DaemonServer>();
            await server.RunAsync(cancellationToken);
            return 0;
        });
        return command;
    }
}