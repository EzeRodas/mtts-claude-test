using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
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

            LinuxVirtualMicService.EnsureCreated();
            return 0;
        });
        
        return command;
    }
}