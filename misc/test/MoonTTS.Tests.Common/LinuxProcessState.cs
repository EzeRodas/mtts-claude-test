using System.Runtime.Versioning;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - Stopped : char
        - Read(int pid) : char?
        - IsGone(int pid) : bool
        - WaitForAsync(int pid, Func<char?, bool> condition, TimeSpan timeout, CancellationToken cancellationToken) : Task<bool>
*/

/* Reads a process's scheduler state from /proc/<pid>/stat, the same source `ps` uses:
   'R' running, 'S' sleeping, 'T' stopped by a signal (SIGSTOP), 'Z' zombie. Linux only */
[SupportedOSPlatform("linux")]
public static class LinuxProcessState
{
    /* State of a process stopped by SIGSTOP */
    public const char Stopped = 'T';

    /* Returns the state letter of a process, or null when the process no longer exists */
    public static char? Read(int pid)
    {
        try
        {
            /* Format: "<pid> (<comm>) <state> ...". comm may contain spaces or ')', so the last ')' is the anchor */
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            int commEnd = stat.LastIndexOf(')');
            return commEnd >= 0 && commEnd + 2 < stat.Length ? stat[commEnd + 2] : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /* True when the process has exited (reaped, zombie or dead) */
    public static bool IsGone(int pid) => Read(pid) is null or 'Z' or 'X';

    /* Polls the process state until the condition holds or the timeout elapses; returns whether it held */
    public static async Task<bool> WaitForAsync(int pid, Func<char?, bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (condition(Read(pid)))
                return true;
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(10, cancellationToken);
        }
    }
}
