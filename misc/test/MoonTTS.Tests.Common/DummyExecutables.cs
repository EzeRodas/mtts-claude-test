using System.Runtime.Versioning;
using System.Text;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - DummyExecutables(TempDirectory root)
        - Directory : string
        - Add(string name, string stdout, int exitCode, bool captureStdin, double sleepSeconds) : DummyExecutables
        - AddScript(string name, string shellBody) : DummyExecutables
        - GetInvocations(string name) : IReadOnlyList<string>
        - GetCapturedStdin(string name) : byte[]
        - WasInvoked(string name) : bool
        - Activate(EnvironmentScope scope) : EnvironmentScope
*/

/* Dummy command-line tools that stand in for OS programs (pactl, wpctl, paplay, pw-play, pw-cli...).
   Each one records its arguments (and optionally its stdin) and answers with canned output, so the app's
   process-driven code runs exactly as it would against the real OS. POSIX only (shell scripts) */
[UnsupportedOSPlatform("windows")]
public sealed class DummyExecutables
{
    private const string InvocationSeparator = "--moontts-invocation--";

    public string Directory { get; }

    /* Creates the bin folder that will be put first on PATH */
    public DummyExecutables(TempDirectory root)
    {
        Directory = root.CreateSubdirectory("dummy-bin");
    }

    /* Adds a tool that prints `stdout`, exits with `exitCode`, optionally saves stdin and optionally sleeps
       (sleeping keeps the process alive, like a player that is still playing) */
    public DummyExecutables Add(string name, string stdout = "", int exitCode = 0, bool captureStdin = false, double sleepSeconds = 0)
    {
        string stdoutFile = System.IO.Path.Combine(Directory, $"{name}.stdout-content");
        File.WriteAllText(stdoutFile, stdout);

        var body = new StringBuilder();
        body.AppendLine($"cat '{stdoutFile}'");
        if (captureStdin)
            body.AppendLine($"cat >> '{System.IO.Path.Combine(Directory, name)}.stdin'");
        if (sleepSeconds > 0)
            body.AppendLine($"sleep {sleepSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        body.AppendLine($"exit {exitCode}");
        return AddScript(name, body.ToString());
    }

    /* Adds a tool with a custom shell body. Arguments are always recorded before the body runs */
    public DummyExecutables AddScript(string name, string shellBody)
    {
        string path = System.IO.Path.Combine(Directory, name);
        string log = path + ".log";
        string script =
            "#!/bin/sh\n" +
            $"echo '{InvocationSeparator}' >> '{log}'\n" +
            $"for a in \"$@\"; do printf '%s\\n' \"$a\" >> '{log}'; done\n" +
            shellBody + "\n";

        File.WriteAllText(path, script);
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return this;
    }

    /* Returns each invocation's arguments joined by spaces, oldest first */
    public IReadOnlyList<string> GetInvocations(string name)
    {
        string log = System.IO.Path.Combine(Directory, name + ".log");
        if (!File.Exists(log))
            return Array.Empty<string>();

        return File.ReadAllText(log)
            .Split(InvocationSeparator + "\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => string.Join(' ', block.Split('\n', StringSplitOptions.RemoveEmptyEntries)))
            .ToList();
    }

    /* Returns every byte the tool received on stdin (captureStdin must be true) */
    public byte[] GetCapturedStdin(string name)
    {
        string path = System.IO.Path.Combine(Directory, name + ".stdin");
        return File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
    }

    /* True when the tool ran at least once */
    public bool WasInvoked(string name) => GetInvocations(name).Count > 0;

    /* Puts the dummy tools first on PATH for the lifetime of the scope */
    public EnvironmentScope Activate(EnvironmentScope scope) => scope.PrependPath(Directory);
}
