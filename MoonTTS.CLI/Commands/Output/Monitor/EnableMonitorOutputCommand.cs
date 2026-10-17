using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;

namespace MoonTTS.CLI.commands.Output.Monitor;

/* Enables monitor playback */

public static class EnableMonitorOutputCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("enable", "Enables the monitor audio output.");
        command.SetAction((_) =>
        {
            var settingsManager = services.GetRequiredService<SettingsManager>();
            var config = settingsManager.CurrentAppConfig;
            config.EnableMonitorPlayback = true;
            settingsManager.SaveAppConfig(config);
            Terminal.Success("Monitor audio output enabled.");
            return 0;
        });
        return command;
    }
}