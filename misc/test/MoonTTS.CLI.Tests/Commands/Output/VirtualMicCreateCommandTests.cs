using System.Runtime.Versioning;
using MoonTTS.CLI.commands.Output;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Output;

/* The virtual mic service shells out to pactl / wpctl / pw-cli; dummy executables first on PATH answer like the
   PulseAudio / PipeWire CLIs would and record every module the command tries to load */
[Collection(TestCollections.Environment)]
[UnsupportedOSPlatform("windows")]
public sealed class VirtualMicCreateCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();
    private readonly TempDirectory _temp = new();
    private readonly EnvironmentScope _environment = new();
    private readonly DummyExecutables _bin;

    /* Puts the dummy audio tools first on PATH; by default no Moon-TTS sink exists and wpctl lists nothing */
    public VirtualMicCreateCommandTests()
    {
        /* xUnit may build the class before evaluating SkipWhen; shell scripts only exist on POSIX */
        _bin = null!;
        if (TestPlatform.IsWindows)
            return;
        _bin = new DummyExecutables(_temp);
        _bin.Activate(_environment);
        _bin.Add("wpctl", " ├─ Sinks:\n");
        _bin.Add("pw-cli", "id 77, type PipeWire:Interface:Node/3\n");
    }

    /* Restores PATH and removes the dummy tools */
    public void Dispose()
    {
        _environment.Dispose();
        _temp.Dispose();
        _host.Dispose();
    }

    /* Dummy pactl: "list sinks short" prints `sinks`; each load-module exits with the given code */
    private void AddPactl(string sinks = "", int nullSinkExit = 0, int remapExit = 0) =>
        _bin.AddScript("pactl", $"""
            case "$1 $2" in
              "list sinks") printf '%s\n' '{sinks}' ;;
              "load-module module-null-sink") [ {nullSinkExit} -eq 0 ] && echo 536870913 || echo 'Failure: Module initialization failed' >&2; exit {nullSinkExit} ;;
              "load-module module-remap-source") [ {remapExit} -eq 0 ] && echo 536870914 || echo 'Failure: No such entity' >&2; exit {remapExit} ;;
            esac
            exit 0
            """);

    /* Every pactl load-module invocation, in order */
    private List<string> LoadedModules() =>
        _bin.GetInvocations("pactl").Where(i => i.StartsWith("load-module", StringComparison.Ordinal)).ToList();

    /* Build creates the "create" command without arguments */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void Build_Always_CreatesCreateCommand()
    {
        var command = VirtualMicCreateCommand.Build(_host.Services);

        Assert.Equal("create", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* A missing sink is created as a PulseAudio null sink named Moon-TTS_Sink */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Create_SinkMissing_LoadsNullSinkModule()
    {
        AddPactl();

        await _host.RunAsync(VirtualMicCreateCommand.Build);

        var nullSink = LoadedModules()[0];
        Assert.StartsWith("load-module module-null-sink", nullSink);
        Assert.Contains("sink_name=Moon-TTS_Sink", nullSink);
    }

    /* After the null sink, its monitor is remapped into a microphone source named Moon-TTS_Mic */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Create_NullSinkLoaded_RemapsMonitorAsMicrophone()
    {
        AddPactl();

        await _host.RunAsync(VirtualMicCreateCommand.Build);

        var modules = LoadedModules();
        Assert.Equal(2, modules.Count);
        Assert.StartsWith("load-module module-remap-source", modules[1]);
        Assert.Contains("master=Moon-TTS_Sink.monitor", modules[1]);
        Assert.Contains("source_name=Moon-TTS_Mic", modules[1]);
    }

    /* Creating the sink and the mic successfully exits with 0 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Create_SinkMissing_ReturnsZero()
    {
        AddPactl();

        var run = await _host.RunAsync(VirtualMicCreateCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.False(_bin.WasInvoked("pw-cli"));
    }

    /* When PulseAudio cannot load the null sink, the sink is created through PipeWire's pw-cli */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Create_PactlCannotLoadNullSink_FallsBackToPwCli()
    {
        AddPactl(nullSinkExit: 1);

        var run = await _host.RunAsync(VirtualMicCreateCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("create-node adapter", Assert.Single(_bin.GetInvocations("pw-cli")));
        Assert.Single(LoadedModules());
    }

    /* An existing sink is left alone: no module is loaded and pw-cli is not run */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Create_SinkAlreadyExists_DoesNotLoadModules()
    {
        AddPactl(sinks: "42\tMoon-TTS_Sink\tmodule-null-sink.c\ts16le 2ch 44100Hz\tIDLE");

        await _host.RunAsync(VirtualMicCreateCommand.Build);

        Assert.Empty(LoadedModules());
        Assert.False(_bin.WasInvoked("pw-cli"));
    }

    /* A failing remap is reported and exits with 1 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Create_RemapFails_ReturnsErrorWithMessage()
    {
        AddPactl(remapExit: 1);

        var run = await _host.RunAsync(VirtualMicCreateCommand.Build);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Failed to remap virtual microphone source", run.Output);
    }

    /* When neither pactl nor pw-cli can create the sink the failure is reported and exits with 1 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Create_EveryToolFails_ReturnsErrorWithMessage()
    {
        AddPactl(nullSinkExit: 1);
        _bin.Add("pw-cli", "", exitCode: 1);

        var run = await _host.RunAsync(VirtualMicCreateCommand.Build);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Failed to create virtual sink via pactl and pw-cli", run.Output);
    }
}
