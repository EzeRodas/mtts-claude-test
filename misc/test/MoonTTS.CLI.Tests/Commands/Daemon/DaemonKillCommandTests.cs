using MoonTTS.CLI.commands.Daemon;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Infrastructure.Daemon.DTOs;

namespace MoonTTS.CLI.Tests.Commands.Daemon;

[Collection(TestCollections.Environment)]
public sealed class DaemonKillCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Build creates the "kill" command without arguments */
    [Fact]
    public void Build_Always_CreatesKillCommand()
    {
        var command = DaemonKillCommand.Build(_host.Services);

        Assert.Equal("kill", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* With no daemon listening the user is told so and the command exits with 1 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Kill_DaemonNotRunning_WarnsAndReturnsOne()
    {
        var run = await _host.RunAsync(DaemonKillCommand.Build);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Daemon is not running.", run.Output);
    }

    /* A running daemon receives the kill request and its server loop ends */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Kill_DaemonRunning_StopsDaemonServer()
    {
        await using var daemon = await CliDummyDaemon.StartAsync();

        await _host.RunAsync(DaemonKillCommand.Build);

        var finished = await Task.WhenAny(daemon.RunTask, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Same(daemon.RunTask, finished);
    }

    /* A running daemon that accepts the kill is reported as stopped with exit code 0 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Kill_DaemonRunning_PrintsStoppedAndReturnsZero()
    {
        await using var daemon = await CliDummyDaemon.StartAsync();

        var run = await _host.RunAsync(DaemonKillCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Daemon stopped successfully.", run.Output);
    }

    /* The request sent over the pipe is the "kill" control action */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Kill_DaemonRunning_SendsKillAction()
    {
        await using var daemon = await CliDummyDaemon.StartScriptedAsync(_ =>
            [CliDummyDaemon.Response(new DaemonResponse { Success = true })]);

        await _host.RunAsync(DaemonKillCommand.Build);

        Assert.Equal(DaemonIpcProtocol.ActionKill, Assert.Single(daemon.Requests).Action);
    }

    /* A daemon that refuses the kill is reported and the command exits with 1 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Kill_DaemonRefuses_WarnsAndReturnsOne()
    {
        await using var daemon = await CliDummyDaemon.StartScriptedAsync(_ =>
            [CliDummyDaemon.Response(new DaemonResponse { Success = false })]);

        var run = await _host.RunAsync(DaemonKillCommand.Build);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Failure to stop daemon.", run.Output);
    }
}
