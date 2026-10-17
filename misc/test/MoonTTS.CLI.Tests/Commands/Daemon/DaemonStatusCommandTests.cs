using System.Text;
using MoonTTS.CLI.commands.Daemon;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Infrastructure.Daemon.DTOs;

namespace MoonTTS.CLI.Tests.Commands.Daemon;

[Collection(TestCollections.Environment)]
public sealed class DaemonStatusCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Starts a scripted daemon answering every status request with `response` */
    private static Task<CliDummyDaemon> StartScriptedAsync(DaemonResponse response) =>
        CliDummyDaemon.StartScriptedAsync(_ => [CliDummyDaemon.Response(response)]);

    /* Build creates the "status" command without arguments */
    [Fact]
    public void Build_Always_CreatesStatusCommand()
    {
        var command = DaemonStatusCommand.Build(_host.Services);

        Assert.Equal("status", command.Name);
        Assert.Empty(command.Arguments);
    }

    /* With no daemon listening the status is STOPPED and the command still succeeds */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_DaemonNotRunning_PrintsStoppedAndReturnsZero()
    {
        var run = await _host.RunAsync(DaemonStatusCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Daemon is currently: STOPPED", run.Output);
    }

    /* A running daemon is shown as RUNNING with its resident model and idle countdown */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_DaemonRunning_PrintsRunningWithActiveModel()
    {
        await using var daemon = await CliDummyDaemon.StartAsync();

        var run = await _host.RunAsync(DaemonStatusCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Daemon Process Status", run.Output);
        string row = CliOutput.LineContaining(run.Output, "RUNNING");
        Assert.Contains(daemon.Model.Id.ToString(), row);
        Assert.Matches(@"\d+m \d+s", row);
    }

    /* The request sent over the pipe is the "status" control action */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_DaemonRunning_SendsStatusAction()
    {
        await using var daemon = await StartScriptedAsync(new DaemonResponse { Success = true });

        await _host.RunAsync(DaemonStatusCommand.Build);

        Assert.Equal(DaemonIpcProtocol.ActionStatus, Assert.Single(daemon.Requests).Action);
    }

    /* The idle countdown is split into minutes and seconds */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_IdleSecondsRemaining_FormatsMinutesAndSeconds()
    {
        await using var daemon = await StartScriptedAsync(new DaemonResponse
        {
            Success = true, ActiveModel = "kokoro/v1", IdleSecondsRemaining = 125
        });

        var run = await _host.RunAsync(DaemonStatusCommand.Build);

        var cells = CliOutput.LineContaining(run.Output, "RUNNING").Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["RUNNING", "kokoro/v1", "2m 5s"], cells);
    }

    /* A daemon without an active model shows a dash */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_NoActiveModel_ShowsDash()
    {
        await using var daemon = await StartScriptedAsync(new DaemonResponse { Success = true, IdleSecondsRemaining = 0 });

        var run = await _host.RunAsync(DaemonStatusCommand.Build);

        var cells = CliOutput.LineContaining(run.Output, "RUNNING").Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["RUNNING", "-", "0m 0s"], cells);
    }

    /* A daemon answering with something other than a Response frame counts as stopped */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Status_DaemonAnswersWithUnexpectedFrame_PrintsStopped()
    {
        await using var daemon = await CliDummyDaemon.StartScriptedAsync(_ => [(DaemonOpcode.Error, Encoding.UTF8.GetBytes("busy"))]);

        var run = await _host.RunAsync(DaemonStatusCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Daemon is currently: STOPPED", run.Output);
    }
}
