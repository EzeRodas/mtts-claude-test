using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;

namespace MoonTTS.CLI.commands.Output.Main;

/* Enables primary playback */

public static class EnableMainOutputCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("enable", "Enables the primary audio output.");
        command.SetAction((_) =>
        {
            var settingsManager = services.GetRequiredService<SettingsManager>();
            var config = settingsManager.CurrentAppConfig;
            config.EnablePrimaryPlayback = true;
            settingsManager.SaveAppConfig(config);
            Terminal.Success("Main audio output enabled.");
            return 0;
        });
        return command;
    }
}