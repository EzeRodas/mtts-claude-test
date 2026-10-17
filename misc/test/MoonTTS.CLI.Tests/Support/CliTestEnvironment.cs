using System.Runtime.CompilerServices;
using MoonTTS.Infrastructure.Daemon.DTOs;

namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - Initialize() : void
        - DaemonSocketPath : string
        - WaitForDaemonSocketAsync() : Task
*/

/* Gives the whole test process its own daemon pipe folder. On Linux/macOS .NET named pipes are Unix sockets under
   Path.GetTempPath() (TMPDIR), and .NET fixes that folder the first time any pipe is used in the process, so it
   cannot be changed per test: it is set here, before any test runs, to a private folder. No test can reach the
   user's real Moon-TTS daemon, and pipe tests share one private socket, so they run in the Environment collection */
internal static class CliTestEnvironment
{
    /* Created in the real temp folder before TMPDIR is redirected to it */
    private static readonly TempDirectory PipeDirectory = new();

    /* Runs when the test assembly loads, before xUnit starts any test */
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("TMPDIR", PipeDirectory.Path + Path.DirectorySeparatorChar);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => PipeDirectory.Dispose();
    }

    /* Socket file a daemon server binds to (exists exactly while a server listens) */
    public static string DaemonSocketPath => Path.Combine(PipeDirectory.Path, "CoreFxPipe_" + DaemonIpcProtocol.PipeName);

    /* Waits until a daemon server is listening without connecting to it (fails the test after 10 s).
       Connecting would consume a server instance, which the real DaemonServer replaces only after a delay */
    public static async Task WaitForDaemonSocketAsync()
    {
        for (int i = 0; i < 200 && !File.Exists(DaemonSocketPath); i++)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(DaemonSocketPath), "No daemon started listening on the test pipe.");
    }
}
