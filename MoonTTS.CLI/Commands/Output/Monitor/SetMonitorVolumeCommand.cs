using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;

namespace MoonTTS.CLI.commands.Output.Monitor;

/* Sets the volume for the monitor audio output */

public static class SetMonitorVolumeCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("vol", "Sets the volume for monitor output.");
        var levelArg = new Argument<int?>("level") { Description = "Volume level from 0 to 100. If omitted, shows current volume.", Arity = ArgumentArity.ZeroOrOne };
        command.Add(levelArg);

        command.SetAction((parseResult) =>
        {
            var settingsManager = services.GetRequiredService<SettingsManager>();
            var config = settingsManager.CurrentAppConfig;
            int? level = parseResult.GetValue(levelArg);

            if (level.HasValue)
            {
                int vol = Math.Clamp(level.Value, 0, 100);
                config.MonitorVolume = vol;
                settingsManager.SaveAppConfig(config);
                Terminal.Success($"Monitor audio volume set to: {vol}%");
            }
            else
            {
                Terminal.Info($"Monitor audio volume is currently: {config.MonitorVolume}%");
            }
            return 0;
        });
        return command;
    }
}