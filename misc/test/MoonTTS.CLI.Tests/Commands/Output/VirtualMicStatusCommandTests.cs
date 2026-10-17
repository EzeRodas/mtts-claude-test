using System.Runtime.Versioning;
using MoonTTS.CLI.commands.Output;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output;

/* The virtual mic service shells out to pactl / wpctl; dummy executables first on PATH answer like the
   PulseAudio / WirePlumber CLIs would, so the real audio server is never touched */
[Collection(TestCollections.Environment)]
[UnsupportedOSPlatform("windows")]
public sealed class VirtualMicStatusCommandTests : IDisposable
{
    private const string MoonSinkLine = "42\tMoon-TTS_Sink\tmodule-null-sink.c\ts16le 2ch 44100Hz\tIDLE";
    private const string OtherSinkLine = "1\talsa_output.pci-0000_00_1f.3.analog-stereo\tmodule-alsa-card.c\ts32le 2ch 48000Hz\tRUNNING";

    private readonly CliTestHost _host = new();
    private readonly TempDirectory _temp = new();
    private readonly EnvironmentScope _environment = new();
    private readonly DummyExecutables _bin;

    /* Puts the dummy audio tools first on PATH */
    public VirtualMicStatusCommandTests()
    {
        /* xUnit may build the class before evaluating SkipWhen; shell scripts only exist on POSIX */
        _bin = null!;
        if (TestPlatform.IsWindows)
            return;
        _bin = new DummyExecutables(_temp);
        _bin.Activate(_environment);
    }

    /* Restores PATH and removes the dummy tools */
    public void Dispose()
    {
        _environment.Dispose();
        _temp.Dispose();
        _host.Dispose();
    }

    /* Dummy pactl whose "list sinks short" prints `sinks` and exits with `exitCode` */
    private void AddPactl(string sinks, int exitCode = 0) => _bin.Add("pactl", sinks + "\n", exitCode);

    /* Dummy wpctl whose "status" prints `status` */
    private void AddWpctl(string status, int exitCode = 0) => _bin.Add("wpctl", status + "\n", exitCode);

    /* Build creates the "status" command without arguments */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void Build_Always_CreatesStatusCommand()
    {
        var command = VirtualMicStatusCommand.Build(_host.Services);

        Assert.Equal("status", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* The sink listed by PulseAudio means the mic is active */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_SinkListedByPactl_PrintsActiveAndReturnsZero()
    {
        AddPactl(OtherSinkLine + "\n" + MoonSinkLine);
        AddWpctl("");

        var run = await _host.RunAsync(VirtualMicStatusCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("The virtual microphone is active.", run.Output);
        Assert.Equal("list sinks short", Assert.Single(_bin.GetInvocations("pactl")));
    }

    /* When PulseAudio lists other sinks only, WirePlumber is asked and its listing counts */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_SinkOnlyInWirePlumber_ReturnsZero()
    {
        AddPactl(OtherSinkLine);
        AddWpctl(" ├─ Sinks:\n │      55. Moon-TTS_Sink                         [vol: 1.00]");

        var run = await _host.RunAsync(VirtualMicStatusCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("status", Assert.Single(_bin.GetInvocations("wpctl")));
    }

    /* A failing pactl (no PulseAudio) falls back to WirePlumber */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_PactlFailsButWirePlumberHasSink_ReturnsZero()
    {
        AddPactl("Connection failure: Connection refused", exitCode: 1);
        AddWpctl(" │      55. Moon-TTS_Sink");

        var run = await _host.RunAsync(VirtualMicStatusCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("The virtual microphone is active.", run.Output);
    }

    /* When neither tool lists the sink the mic is reported inactive with exit code 1 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_NoSink_WarnsAndReturnsOne()
    {
        AddPactl(OtherSinkLine);
        AddWpctl(" ├─ Sinks:\n │  *   48. Built-in Audio Analog Stereo");

        var run = await _host.RunAsync(VirtualMicStatusCommand.Build);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The virtual microphone is not active.", run.Output);
    }

    /* A sink listed by a tool that exits with an error does not count */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_SinkPrintedByFailingTools_ReturnsOne()
    {
        AddPactl(MoonSinkLine, exitCode: 1);
        AddWpctl("Moon-TTS_Sink", exitCode: 1);

        var run = await _host.RunAsync(VirtualMicStatusCommand.Build);

        Assert.Equal(1, run.ExitCode);
    }
}
