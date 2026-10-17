using MoonTTS.CLI.commands.Daemon;
using MoonTTS.CLI.Tests.Support;

namespace MoonTTS.CLI.Tests.Commands.Daemon;

/* Only the "already running" branch is exercised: every other branch calls DaemonClient.EnsureRunningAsync, which
   spawns Environment.ProcessPath (the test host itself) with "daemon run", and the client offers no seam to stop it */
[Collection(TestCollections.Environment)]
public sealed class DaemonStartCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "start" command without arguments */
    [Fact]
    public void Build_Always_CreatesStartCommand()
    {
        var command = DaemonStartCommand.Build(_host.Services);

        Assert.Equal("start", command.Name);
        Assert.Empty(command.Arguments);
        Assert.False(command.Hidden);
    }

    /* A daemon that is already listening is reported and nothing new is started */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Start_DaemonAlreadyRunning_WarnsAndReturnsZero()
    {
        await using var daemon = await CliDummyDaemon.StartAsync();

        var run = await _host.RunAsync(DaemonStartCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Moon-TTS daemon is already running.", run.Output);
        Assert.DoesNotContain("Starting daemon", run.Output);
    }
}
