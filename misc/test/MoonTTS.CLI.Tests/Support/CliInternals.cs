using System.CommandLine;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.Domain.Entities;

namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - RunMain(object? program, string[] args) : Task<int>
        - ConfigureServices(object? program, IServiceCollection services) : void
        - BuildRootCommand(object? program, IServiceProvider sp) : RootCommand
        - FindDevice(object? deviceFinder, List<AudioDeviceInfo> devices, string query) : AudioDeviceInfo?
*/

/* Compile-time bound accessors to the CLI's internal members (Program and DeviceFinder are internal to the mtts
   assembly). UnsafeAccessor binds by name without reflection, so production code needs no InternalsVisibleTo.
   The first parameter only names the declaring type; pass null */
internal static class CliInternals
{
    private const string ProgramType = "MoonTTS.CLI.Program, mtts";
    private const string DeviceFinderType = "MoonTTS.CLI.commands.Output.DeviceFinder, mtts";

    /* Program.Main: the real entry point (DI container, configuration load, parse and invoke) */
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Main")]
    public static extern Task<int> RunMain([UnsafeAccessorType(ProgramType)] object? program, string[] args);

    /* Program.ConfigureServices: registers the production services */
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ConfigureServices")]
    public static extern void ConfigureServices([UnsafeAccessorType(ProgramType)] object? program, IServiceCollection services);

    /* Program.BuildRootCommand: builds the complete command tree over a service provider */
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "BuildRootCommand")]
    public static extern RootCommand BuildRootCommand([UnsafeAccessorType(ProgramType)] object? program, IServiceProvider sp);

    /* DeviceFinder.FindDevice: resolves a device by id, exact name or partial name */
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FindDevice")]
    public static extern AudioDeviceInfo? FindDevice(
        [UnsafeAccessorType(DeviceFinderType)] object? deviceFinder, List<AudioDeviceInfo> devices, string query);
}
