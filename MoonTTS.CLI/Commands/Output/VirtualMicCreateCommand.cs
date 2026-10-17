using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Infrastructure.Audio.Linux;

namespace MoonTTS.CLI.commands.Output;

public class VirtualMicCreateCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("create", "Creates a virtual sink and microphone to route the output audio through.");
        command.SetAction((_) =>
        {
            if (LinuxVirtualMicService.Exists())
                return 1;

            /* The sink can fail to be created (no pactl / pw-cli, module load refused); report it instead of exiting 0 */
            var createResult = LinuxVirtualMicService.EnsureCreated();
            if (createResult.IsFailure)
            {
                Terminal.Error($"Error: {createResult.ErrorMessage}");
                return 1;
            }
            return 0;
        });
        
        return command;
    }
}