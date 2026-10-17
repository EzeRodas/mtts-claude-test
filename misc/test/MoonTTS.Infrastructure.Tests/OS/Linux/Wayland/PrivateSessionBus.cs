using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace MoonTTS.Infrastructure.Tests.OS.Linux.Wayland;

/* File Structure:
        - Address : string
        - StartAsync(CancellationToken cancellationToken) : Task<PrivateSessionBus>
        - Dispose() : void
*/

/* A private D-Bus session bus (dbus-daemon) that stands in for the user's session bus.
   The daemon uses a minimal configuration without service directories, so no real service (such as the real
   xdg-desktop-portal) can ever be auto-started on it. Tmds.DBus caches DBusAddress.Session process-wide on
   first use, so every bus started by this test process listens on the same fixed socket path; tests are skipped
   when the cache already points somewhere else, which guarantees the real session bus is never touched */
internal sealed class PrivateSessionBus : IDisposable
{
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "moontts-tests", $"dbus-{Environment.ProcessId}");
    private static readonly string SocketPath = Path.Combine(Root, "bus");

    private readonly Process _daemon;
    private readonly EnvironmentScope _environment;

    /* The address hooks connect to through DBUS_SESSION_BUS_ADDRESS */
    public static string Address { get; } = "unix:path=" + SocketPath;

    /* Wraps a running daemon */
    private PrivateSessionBus(Process daemon, EnvironmentScope environment)
    {
        _daemon = daemon;
        _environment = environment;
    }

    /* Points the session bus address at the private bus and starts dbus-daemon, returning once it accepts
       connections */
    public static async Task<PrivateSessionBus> StartAsync(CancellationToken cancellationToken)
    {
        var environment = new EnvironmentScope().Set("DBUS_SESSION_BUS_ADDRESS", Address);
        if (DBusAddress.Session != Address)
        {
            environment.Dispose();
            Assert.Skip($"Tmds.DBus already cached the session bus address '{DBusAddress.Session}'; refusing to use it.");
        }

        Directory.CreateDirectory(Root);
        File.Delete(SocketPath);
        string config = Path.Combine(Root, "session.conf");
        await File.WriteAllTextAsync(config, BuildConfig(), cancellationToken);

        var startInfo = new ProcessStartInfo("dbus-daemon")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add($"--config-file={config}");
        startInfo.ArgumentList.Add("--nofork");
        startInfo.ArgumentList.Add("--print-address");

        var daemon = Process.Start(startInfo)!;
        try
        {
            /* dbus-daemon prints its address once it is listening */
            string? printed = await daemon.StandardOutput.ReadLineAsync(cancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (string.IsNullOrEmpty(printed))
                throw new InvalidOperationException($"dbus-daemon did not start: {await daemon.StandardError.ReadToEndAsync(cancellationToken)}");
        }
        catch
        {
            Kill(daemon);
            environment.Dispose();
            throw;
        }

        return new PrivateSessionBus(daemon, environment);
    }

    /* Stops the daemon (disconnecting every client), removes its socket and config, and restores the environment */
    public void Dispose()
    {
        Kill(_daemon);
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            /* The next bus recreates the folder and deletes a stale socket before listening */
        }
        _environment.Dispose();
    }

    /* Kills and reaps the daemon */
    private static void Kill(Process daemon)
    {
        try
        {
            if (!daemon.HasExited)
                daemon.Kill(entireProcessTree: true);
            daemon.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            /* Already gone */
        }
        daemon.Dispose();
    }

    /* Session bus configuration: one private socket, everyone allowed, no service activation */
    private static string BuildConfig() =>
        "<!DOCTYPE busconfig PUBLIC \"-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN\"\n" +
        " \"http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd\">\n" +
        "<busconfig>\n" +
        "  <type>session</type>\n" +
        $"  <listen>unix:path={SocketPath}</listen>\n" +
        "  <auth>EXTERNAL</auth>\n" +
        "  <policy context=\"default\">\n" +
        "    <allow send_destination=\"*\" eavesdrop=\"true\"/>\n" +
        "    <allow eavesdrop=\"true\"/>\n" +
        "    <allow own=\"*\"/>\n" +
        "  </policy>\n" +
        "</busconfig>\n";
}
