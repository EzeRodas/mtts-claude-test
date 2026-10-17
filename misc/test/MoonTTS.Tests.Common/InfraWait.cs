using System.Diagnostics;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - UntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken) : Task<bool>
        - IsProcessRunning(int processId) : bool
        - ProcessExitsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken) : Task<bool>
*/

/* Bounded waits for conditions the code under test reaches asynchronously (a worker being killed, a socket
   starting to listen). They return as soon as the condition holds, so tests never rely on fixed sleeps */
public static class InfraWait
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    /* Polls the condition until it is true or the timeout elapses. Returns the last evaluation */
    public static async Task<bool> UntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed >= timeout)
                return condition();
            await Task.Delay(PollInterval, cancellationToken);
        }
        return true;
    }

    /* True while a process with this id exists and has not terminated (zombies count as terminated) */
    public static bool IsProcessRunning(int processId)
    {
        if (OperatingSystem.IsLinux())
        {
            string statPath = $"/proc/{processId}/stat";
            try
            {
                string stat = File.ReadAllText(statPath);
                int afterName = stat.LastIndexOf(')');
                return afterName >= 0 && afterName + 2 < stat.Length && stat[afterName + 2] != 'Z';
            }
            catch (IOException)
            {
                return false;
            }
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /* Waits until the process is gone */
    public static Task<bool> ProcessExitsAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        UntilAsync(() => !IsProcessRunning(processId), timeout, cancellationToken);
}
