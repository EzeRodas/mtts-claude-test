using System.CommandLine;
using System.IO.Pipes;
using System.Text.Json;
using MoonTTS.CLI.commands.Daemon;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Infrastructure.Daemon;
using MoonTTS.Infrastructure.Daemon.DTOs;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.CLI.Tests.Commands.Daemon;

/* "daemon run" is the daemon process itself; here it runs in-process (the real DaemonServer.RunAsync over fakes) on
   the test process' private pipe. Readiness is detected by the socket file, and requests are sent on a single
   connection: DaemonServer serves one pipe instance at a time and drops a connection that arrives right after
   another one (see the report), so the tests avoid the probe-then-request pattern against it */
[Collection(TestCollections.Environment)]
public sealed class DaemonRunCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* A "daemon run" invocation in progress */
    private sealed record RunningDaemon(Task<int> Invocation, CliConsoleCapture Console, CancellationTokenSource Cts);

    /* Starts "run" in the background and waits until its pipe is bound */
    private async Task<RunningDaemon> StartRunAsync()
    {
        var console = new CliConsoleCapture();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var root = new RootCommand { DaemonRunCommand.Build(_host.Services) };
        var invocation = root.Parse(["run"]).InvokeAsync(CliRun.Configuration(console), cts.Token);
        var running = new RunningDaemon(invocation, console, cts);

        try
        {
            await CliTestEnvironment.WaitForDaemonSocketAsync();
        }
        catch
        {
            await running.Cts.CancelAsync();
            await FinishAsync(running);
            throw;
        }
        return running;
    }

    /* Waits for the invocation (10 s at most) and releases its console and token */
    private static async Task<int> FinishAsync(RunningDaemon running)
    {
        try
        {
            return await running.Invocation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            running.Console.Dispose();
            running.Cts.Dispose();
        }
    }

    /* Sends one control request on its own connection and returns the daemon's response */
    private static async Task<DaemonResponse?> SendRequestAsync(string action)
    {
        var token = TestContext.Current.CancellationToken;
        await using var client = new NamedPipeClientStream(".", DaemonIpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000, token);
        byte[] request = JsonSerializer.SerializeToUtf8Bytes(new DaemonRequest { Action = action }, AppJsonContext.Default.DaemonRequest);
        await DaemonIpcProtocol.WriteFrameAsync(client, DaemonOpcode.Request, request, token);
        var (opcode, payload) = await DaemonIpcProtocol.ReadFrameAsync(client, token);
        Assert.Equal(DaemonOpcode.Response, opcode);
        return JsonSerializer.Deserialize(payload, AppJsonContext.Default.DaemonResponse);
    }

    /* Build creates the hidden "run" command (internal, used by auto-spawn) */
    [Fact]
    public void Build_Always_CreatesHiddenRunCommand()
    {
        var command = DaemonRunCommand.Build(_host.Services);

        Assert.Equal("run", command.Name);
        Assert.True(command.Hidden);
        Assert.Empty(command.Arguments);
    }

    /* While running, the daemon pipe accepts clients */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Run_Started_ListensOnDaemonPipe()
    {
        var running = await StartRunAsync();
        try
        {
            Assert.True(await new DaemonClient().IsRunningAsync(1000));
        }
        finally
        {
            await running.Cts.CancelAsync();
            await FinishAsync(running);
        }
    }

    /* The daemon answers status requests with the model selected in settings */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Run_StatusRequested_ReportsActiveModel()
    {
        var model = _host.AddDownloadedModel("kokoro", "v1");
        _host.ActivateModel(model);
        var running = await StartRunAsync();
        try
        {
            var status = await SendRequestAsync(DaemonIpcProtocol.ActionStatus);

            Assert.True(status!.Success);
            Assert.Equal("kokoro/v1", status.ActiveModel);
        }
        finally
        {
            await running.Cts.CancelAsync();
            await FinishAsync(running);
        }
    }

    /* The active model is loaded before the first client connects */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Run_Started_PrewarmsActiveModel()
    {
        var model = _host.AddDownloadedModel("kokoro", "v1");
        _host.ActivateModel(model);

        var running = await StartRunAsync();
        await running.Cts.CancelAsync();
        await FinishAsync(running);

        Assert.Equal(model.Id, Assert.Single(_host.EngineFactory.CreatedModels));
    }

    /* A kill request is acknowledged, ends the loop and the command exits with 0 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Run_KillRequested_ReturnsZero()
    {
        var running = await StartRunAsync();

        var kill = await SendRequestAsync(DaemonIpcProtocol.ActionKill);
        int exitCode = await FinishAsync(running);

        Assert.True(kill!.Success);
        Assert.Equal(0, exitCode);
    }

    /* Cancelling the invocation (Ctrl+C) ends the loop and the command exits with 0 */
    [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task Run_Cancelled_ReturnsZero()
    {
        var running = await StartRunAsync();

        await running.Cts.CancelAsync();
        int exitCode = await FinishAsync(running);

        Assert.Equal(0, exitCode);
    }
}
