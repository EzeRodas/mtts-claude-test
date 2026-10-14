using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;

namespace MoonTTS.CLI.commands.Output.Main;

/* Sets the volume for the main audio output */

public static class SetMainVolumeCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("vol", "Sets the volume for primary output.");
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
                config.PrimaryVolume = vol;
                settingsManager.SaveAppConfig(config);
                Terminal.Success($"Main audio volume set to: {vol}%");
            }
            else
            {
                Terminal.Info($"Main audio volume is currently: {config.PrimaryVolume}%");
            }
            return 0;
        });
        return command;
    }
}