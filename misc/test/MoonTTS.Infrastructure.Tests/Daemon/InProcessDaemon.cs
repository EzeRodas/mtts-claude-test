using MoonTTS.Core.Services;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Daemon;

namespace MoonTTS.Infrastructure.Tests.Daemon;

/* File Structure:
        - InProcessDaemon()
        - Model : ModelId
        - Repository, Settings, Dictionary, Factory, Engine, Provider, Server
        - RunTask : Task?
        - StartAsync(DaemonPipeScope pipe, CancellationToken cancellationToken) : Task
        - DisposeAsync() : ValueTask
*/

/* A DaemonServer running inside the test process over fakes (FakeTtsEngineFactory behind a real TtsEngineProvider),
   listening on the pipe of a DaemonPipeScope */
public sealed class InProcessDaemon : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stop = new();

    /* The model selected in settings when the daemon starts */
    public static ModelId Model { get; } = new("fake", "model");

    public FakeConfigurationRepository Repository { get; } = new();
    public SettingsManager Settings { get; }
    public DictionaryManager Dictionary { get; }
    public FakeTtsEngineFactory Factory { get; } = new();
    public FakeTtsEngine Engine { get; } = new() { OutputFormat = new AudioFormat(24000, 1, 16), ChunksPerRequest = 2, ChunkSize = 100 };
    public TtsEngineProvider Provider { get; }
    public DaemonServer Server { get; }
    public Task? RunTask { get; private set; }

    /* Wires the server with Model selected and served by Engine */
    public InProcessDaemon()
    {
        Repository.AppConfig.ActiveModel = Model;
        Factory.Engines[Model] = () => Engine;
        Settings = new SettingsManager(Repository);
        Dictionary = new DictionaryManager(Repository);
        Provider = new TtsEngineProvider(Factory, Settings);
        Server = new DaemonServer(Provider, Dictionary, Settings);
    }

    /* Runs the server and returns once its pipe exists */
    public async Task StartAsync(DaemonPipeScope pipe, CancellationToken cancellationToken)
    {
        RunTask = Server.RunAsync(_stop.Token);
        bool listening = await InfraWait.UntilAsync(() => File.Exists(pipe.SocketPath) || RunTask.IsCompleted, StartTimeout, cancellationToken);
        if (!listening || RunTask.IsCompleted)
            throw new InvalidOperationException("The in-process daemon did not start listening.");
    }

    /* Stops the server and waits for RunAsync to return */
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (RunTask != null)
            await RunTask.WaitAsync(StopTimeout);
        Provider.Dispose();
        _stop.Dispose();
    }
}
