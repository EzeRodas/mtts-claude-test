using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;

namespace MoonTTS.CLI.commands.Output.Main;

/* Disables primary playback */

public static class DisableMainOutputCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("disable", "Disables the primary audio output.");
        command.SetAction((_) =>
        {
            var settingsManager = services.GetRequiredService<SettingsManager>();
            var config = settingsManager.CurrentAppConfig;
            config.EnablePrimaryPlayback = false;
            settingsManager.SaveAppConfig(config);
            Terminal.Success("Main audio output disabled.");
            return 0;
        });
        return command;
    }
}