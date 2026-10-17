using System.IO.Pipes;
using System.Runtime.Versioning;
using MoonTTS.Avalonia.Services;
using MoonTTS.Avalonia.Tests.TestSupport;

namespace MoonTTS.Avalonia.Tests.Services;

/* Verifies the single-instance named pipe. On Linux/macOS .NET named pipes are Unix sockets under TMPDIR, which
   GuiIsolationFixture points at a private folder for the whole run, so the dummy "other instance" and the code
   under test meet there and a real running Moon-TTS is never contacted. The pipe name is global, so these tests
   run in the non-parallel Environment collection */
[Collection(TestCollections.Environment)]
[UnsupportedOSPlatform("windows")]
public sealed class SingleInstanceServiceTests : IDisposable
{
    /* Stops every server a test starts, even when the test fails */
    private readonly CancellationTokenSource _stop = new();

    /* The pipe name the GUI uses */
    private static string PipeName => (string)GuiPrivate.GetStaticField(typeof(SingleInstanceService), "PipeName")!;

    /* Socket file .NET binds for the pipe inside the isolated TMPDIR */
    private static string SocketPath => Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + PipeName);

    /* A listening pipe standing in for an already running instance */
    private static NamedPipeServerStream ListenAsRunningInstance() =>
        new(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    /* Stops the servers started by the test */
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    /* ---------- TrySendToggleToExistingInstance ---------- */

    /* The pipe lives in the isolated temp folder, never in the real one */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void Pipe_Created_LivesInIsolatedTempFolder()
    {
        using var instance = ListenAsRunningInstance();

        Assert.Contains("moontts-tests", SocketPath);
        Assert.True(File.Exists(SocketPath));
    }

    /* With no instance running the caller is told to start normally */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void TrySendToggle_NoInstanceRunning_ReturnsFalse()
    {
        Assert.False(SingleInstanceService.TrySendToggleToExistingInstance());
    }

    /* With an instance listening the caller is told to exit */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void TrySendToggle_InstanceListening_ReturnsTrue()
    {
        using var instance = ListenAsRunningInstance();

        Assert.True(SingleInstanceService.TrySendToggleToExistingInstance());
    }

    /* The running instance receives the "toggle" command */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task TrySendToggle_InstanceListening_SendsToggleCommand()
    {
        using var instance = ListenAsRunningInstance();

        SingleInstanceService.TrySendToggleToExistingInstance();

        await instance.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(instance);
        Assert.Equal("toggle", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    /* ---------- StartServer ---------- */

    /* A second launch's "toggle" reaches the running instance's callback */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StartServer_ToggleReceived_InvokesCallback()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        /* The callback stops the server itself so it creates no further pipe */
        SingleInstanceService.StartServer(() => { _stop.Cancel(); received.TrySetResult(); }, _stop.Token);
        await Eventually.UntilAsync(SingleInstanceService.TrySendToggleToExistingInstance, "the server accepts a toggle");

        await received.Task.WaitAsync(Eventually.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    /* Sends `command` as another launch would and returns once the server has handled it and closed the pipe */
    private static async Task SendAndWaitUntilHandledAsync(string command)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        await client.ConnectAsync((int)Eventually.DefaultTimeout.TotalMilliseconds, TestContext.Current.CancellationToken);
        client.Write(System.Text.Encoding.UTF8.GetBytes(command + "\n"));
        client.Flush();

        /* Writing fails with a broken pipe once the server has read the line and disposed its end */
        await Eventually.UntilAsync(() =>
        {
            try
            {
                client.WriteByte((byte)'\n');
                client.Flush();
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }, "the server handled the command");
    }

    /* Commands other than "toggle" do not invoke the callback */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StartServer_UnknownCommand_DoesNotInvokeCallback()
    {
        int calls = 0;
        SingleInstanceService.StartServer(() => Interlocked.Increment(ref calls), _stop.Token);

        await SendAndWaitUntilHandledAsync("show");

        Assert.Equal(0, Volatile.Read(ref calls));
    }

    /* After an unknown command the server keeps listening for the next launch */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StartServer_AfterUnknownCommand_KeepsListening()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SingleInstanceService.StartServer(() => { _stop.Cancel(); received.TrySetResult(); }, _stop.Token);
        await SendAndWaitUntilHandledAsync("show");

        /* Retried because the server re-creates its pipe after every connection */
        await Eventually.UntilAsync(() =>
        {
            if (!received.Task.IsCompleted)
                SingleInstanceService.TrySendToggleToExistingInstance();
            return received.Task.IsCompleted;
        }, "a later toggle reaches the callback");
    }

    /* A token cancelled before start means no server is started */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public void StartServer_TokenAlreadyCancelled_DoesNotListen()
    {
        _stop.Cancel();

        SingleInstanceService.StartServer(() => { }, _stop.Token);

        Assert.False(SingleInstanceService.TrySendToggleToExistingInstance());
    }

    /* Cancelling the token while waiting for a connection shuts the server down */
    [Fact(Skip = "Unix socket pipes only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]
    public async Task StartServer_CancelledWhileWaiting_StopsListening()
    {
        int calls = 0;
        SingleInstanceService.StartServer(() => calls++, _stop.Token);
        await Eventually.UntilAsync(() => File.Exists(SocketPath), "the server is listening");

        _stop.Cancel();
        await Eventually.UntilAsync(() => !File.Exists(SocketPath), "the server closed its pipe");

        Assert.False(SingleInstanceService.TrySendToggleToExistingInstance());
        Assert.Equal(0, calls);
    }
}
