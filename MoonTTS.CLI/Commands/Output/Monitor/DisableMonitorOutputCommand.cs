using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;

namespace MoonTTS.CLI.commands.Output.Monitor;

/* Disables monitor playback */

public static class DisableMonitorOutputCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("disable", "Disables the monitor audio output.");
        command.SetAction((_) =>
        {
            var settingsManager = services.GetRequiredService<SettingsManager>();
            var config = settingsManager.CurrentAppConfig;
            config.EnableMonitorPlayback = false;
            settingsManager.SaveAppConfig(config);
            Terminal.Success("Monitor audio output disabled.");
            return 0;
        });
        return command;
    }
}