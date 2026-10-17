using System.IO.Pipes;
using MoonTTS.Infrastructure.Daemon.DTOs;

namespace MoonTTS.Infrastructure.Tests.Daemon;

/* File Structure:
        - DaemonPipeScope()
        - SocketPath : string
        - CreatePipeDirectory() : string
        - PinPipeDirectory(string directory) : void
        - Dispose() : void
*/

/* Isolates the daemon's named pipe from the real daemon. On Linux/macOS .NET named pipes are Unix sockets created
   under Path.GetTempPath() (TMPDIR), and .NET caches that folder the first time any pipe is used in the process,
   so later TMPDIR changes are ignored. Every scope therefore shares one private folder per test process: TMPDIR
   points at it while the scope is active, and the first scope pins .NET's cached pipe folder to it (verified).
   Tests run one at a time in [Collection(TestCollections.Environment)], so sharing the folder is safe */
public sealed class DaemonPipeScope : IDisposable
{
    /* Unix socket paths are limited to ~104 bytes on macOS and 108 on Linux */
    private const int MaxSocketPathLength = 100;

    private static readonly Lock PinLock = new();
    private static readonly Lazy<string> PipeDirectory = new(CreatePipeDirectory);
    private static bool _pinned;

    private readonly EnvironmentScope _environment;

    /* Points TMPDIR at the private pipe folder and removes a socket file left behind by an earlier test */
    public DaemonPipeScope()
    {
        string directory = PipeDirectory.Value;
        _environment = new EnvironmentScope().Set("TMPDIR", directory);
        PinPipeDirectory(directory);

        if (File.Exists(SocketPath))
            File.Delete(SocketPath);
    }

    /* The Unix socket backing the daemon pipe */
    public string SocketPath => Path.Combine(PipeDirectory.Value, "CoreFxPipe_" + DaemonIpcProtocol.PipeName);

    /* Creates the folder in the real temp folder (before TMPDIR changes) and deletes it when the process exits */
    private static string CreatePipeDirectory()
    {
        var temp = new TempDirectory();
        string directory = temp.Path;
        if (Path.Combine(directory, "CoreFxPipe_" + DaemonIpcProtocol.PipeName).Length > MaxSocketPathLength)
            directory = temp.CreateSubdirectory("..", "..", "p" + Guid.NewGuid().ToString("N")[..8]);

        string created = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            temp.Dispose();
            try { Directory.Delete(created, recursive: true); }
            catch (IOException) { /* Already removed with the temp folder */ }
        };
        return created;
    }

    /* Creates a throwaway pipe so .NET caches the private folder, and fails loudly when an earlier pipe already
       pinned another folder (the daemon tests would otherwise talk to the wrong socket) */
    private static void PinPipeDirectory(string directory)
    {
        lock (PinLock)
        {
            if (_pinned)
                return;

            string name = "moontts-pin-" + Guid.NewGuid().ToString("N")[..8];
            using (new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                if (!File.Exists(Path.Combine(directory, "CoreFxPipe_" + name)))
                    throw new InvalidOperationException(
                        $"Named pipes of this test process are not created in '{directory}': another pipe was created before DaemonPipeScope.");
            }
            _pinned = true;
        }
    }

    /* Restores TMPDIR (the pipe folder stays pinned for the next scope) */
    public void Dispose() => _environment.Dispose();
}
