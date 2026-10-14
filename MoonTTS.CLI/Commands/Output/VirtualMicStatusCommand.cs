using System.CommandLine;
using MoonTTS.CLI.Utilities;
using MoonTTS.Infrastructure.Audio.Linux;

namespace MoonTTS.CLI.commands.Output;

public class VirtualMicStatusCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("status", "Checks if there's an active virtual sink for the app.");
        command.SetAction((_) =>
        {
            if (!LinuxVirtualMicService.Exists())
            {
                Terminal.Warning("The virtual microphone is not active.");
                return 1;
            }
        
            Terminal.Success("The virtual microphone is active.");
            return 0;
        });
        
        return command;
    }
}