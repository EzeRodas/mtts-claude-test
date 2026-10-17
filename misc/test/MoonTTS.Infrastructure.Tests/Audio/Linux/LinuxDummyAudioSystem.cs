using System.Diagnostics;
using System.Runtime.Versioning;

namespace MoonTTS.Infrastructure.Tests.Audio.Linux;

/* File Structure:
        - LinuxDummyAudioSystem()
        - Tools : DummyExecutables
        - Temp : TempDirectory
        - AddPlayer(string name, string sinkFlag, double lingerSeconds) : LinuxDummyAudioSystem
        - AddTool(string name, string stdout, int exitCode) : LinuxDummyAudioSystem
        - AddFailingTool(string name, string stderr, int exitCode) : LinuxDummyAudioSystem
        - GetPlayedBytes(string name, string sink) : byte[]
        - GetPlayerPids(string name) : IReadOnlyList<int>
        - WaitForPlayersAsync(string name, int count, CancellationToken cancellationToken) : Task<IReadOnlyList<int>>
        - WaitForPlayedBytesAsync(string name, string sink, int count, CancellationToken cancellationToken) : Task<bool>
        - Dispose() : void
*/

/* A dummy PipeWire / PulseAudio installation: PATH only holds the dummy tools plus the few shell utilities the
   dummies need (cat, sleep), so the real pactl / wpctl / paplay / pw-play / pw-cli are never reachable and a tool
   that is not added behaves exactly like one that is not installed */
[SupportedOSPlatform("linux")]
internal sealed class LinuxDummyAudioSystem : IDisposable
{
    public const string DefaultSink = "default";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly EnvironmentScope _environment;

    public TempDirectory Temp { get; }
    public DummyExecutables Tools { get; }

    /* Builds the isolated tool folders and puts them (and nothing else) on PATH */
    public LinuxDummyAudioSystem()
    {
        Temp = new TempDirectory();
        Tools = new DummyExecutables(Temp);

        string systemBin = Temp.CreateSubdirectory("system-bin");
        foreach (string utility in new[] { "cat", "sleep" })
            File.CreateSymbolicLink(Path.Combine(systemBin, utility), ResolveFromPath(utility));

        _environment = new EnvironmentScope().Set("PATH", Tools.Directory + Path.PathSeparator + systemBin);
    }

    /* Adds a player (paplay / pw-play) that records its pid, saves its stdin to "<name>.<sink>.stdin" (sink is the
       value after sinkFlag, or "default") and, like a real player draining its buffer, optionally lingers
       after stdin closes */
    public LinuxDummyAudioSystem AddPlayer(string name, string sinkFlag, double lingerSeconds = 0)
    {
        string prefix = Path.Combine(Tools.Directory, name);
        string linger = lingerSeconds > 0
            ? $"sleep {lingerSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n"
            : string.Empty;

        Tools.AddScript(name,
            $"sink={DefaultSink}\n" +
            "prev=\n" +
            "for a in \"$@\"; do\n" +
            $"  if [ \"$prev\" = '{sinkFlag}' ]; then sink=\"$a\"; fi\n" +
            "  prev=\"$a\"\n" +
            "done\n" +
            $"echo $$ >> '{prefix}.pids'\n" +
            $"cat > '{prefix}.'\"$sink\"'.stdin'\n" +
            linger);
        return this;
    }

    /* Adds a tool that prints canned output and exits with the given code */
    public LinuxDummyAudioSystem AddTool(string name, string stdout = "", int exitCode = 0)
    {
        Tools.Add(name, stdout, exitCode);
        return this;
    }

    /* Adds a tool that prints an error on stderr and fails, like pactl without a running sound server */
    public LinuxDummyAudioSystem AddFailingTool(string name, string stderr = "Connection failure: Connection refused", int exitCode = 1)
    {
        Tools.AddScript(name, $"echo '{stderr}' >&2\nexit {exitCode}");
        return this;
    }

    /* Returns every byte a player received for a sink */
    public byte[] GetPlayedBytes(string name, string sink = DefaultSink)
    {
        string path = Path.Combine(Tools.Directory, $"{name}.{sink}.stdin");
        return File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
    }

    /* Returns the pids of every player process started so far, oldest first */
    public IReadOnlyList<int> GetPlayerPids(string name)
    {
        string path = Path.Combine(Tools.Directory, name + ".pids");
        if (!File.Exists(path))
            return Array.Empty<int>();

        return File.ReadAllLines(path)
            .Where(line => int.TryParse(line, out _))
            .Select(int.Parse)
            .ToList();
    }

    /* Waits until `count` player processes have started and returns their pids */
    public async Task<IReadOnlyList<int>> WaitForPlayersAsync(string name, int count, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (GetPlayerPids(name).Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(10, cancellationToken);

        var pids = GetPlayerPids(name);
        Assert.True(pids.Count >= count, $"Expected {count} '{name}' process(es), {pids.Count} started.");
        return pids;
    }

    /* Waits until a player has received at least `count` bytes for a sink */
    public async Task<bool> WaitForPlayedBytesAsync(string name, string sink, int count, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (GetPlayedBytes(name, sink).Length < count && DateTime.UtcNow < deadline)
            await Task.Delay(10, cancellationToken);
        return GetPlayedBytes(name, sink).Length >= count;
    }

    /* Kills any dummy player still alive, restores PATH and deletes the folders */
    public void Dispose()
    {
        foreach (string pidFile in Directory.GetFiles(Tools.Directory, "*.pids"))
        {
            foreach (string line in File.ReadAllLines(pidFile))
            {
                if (!int.TryParse(line, out int pid) || LinuxProcessState.IsGone(pid))
                    continue;
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                }
                catch (ArgumentException)
                {
                    /* Exited between the check and the kill */
                }
                catch (InvalidOperationException)
                {
                    /* Exited between the check and the kill */
                }
            }
        }

        _environment.Dispose();
        Temp.Dispose();
    }

    /* Finds a utility on the current PATH (before it is replaced) */
    private static string ResolveFromPath(string utility)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(dir, utility);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException($"'{utility}' is needed by the dummy audio tools but is not on PATH.");
    }
}
