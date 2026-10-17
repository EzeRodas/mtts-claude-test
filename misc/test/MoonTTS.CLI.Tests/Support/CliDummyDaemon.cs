using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Daemon;
using MoonTTS.Infrastructure.Daemon.DTOs;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - StartAsync(bool withModel, Action<FakeTtsEngine>? configureEngine) : Task<CliDummyDaemon>
        - StartScriptedAsync(Func<DaemonRequest, IReadOnlyList<(DaemonOpcode Opcode, byte[] Payload)>> respond) : Task<CliDummyDaemon>
        - Response(DaemonResponse response) : (DaemonOpcode, byte[])
        - Engine : FakeTtsEngine
        - Model : ModelMetadata
        - Requests : IReadOnlyList<DaemonRequest>
        - RunTask : Task
        - ServeAsync(CancellationToken token) : Task
        - HandleAsync(Stream stream, CancellationToken token) : Task
        - RespondAsync(Stream stream, DaemonRequest request, CancellationToken token) : Task
        - CreateInstance() : NamedPipeServerStream
        - DisposeAsync() : ValueTask
*/

/* The background daemon a user would have running, hosted in-process on the test pipe (nothing is spawned, so the
   test host is never started as "mtts"). It answers like DaemonServer: "synthesize" is served by a real
   DaemonServer.HandleSynthesizeAsync over fakes (FakeTtsEngine audio, real framing), "status" and "kill" with the
   same responses DaemonServer sends. Unlike DaemonServer.RunAsync it keeps the next pipe instance listening while it
   handles a client, so the CLI's probe-then-request pattern (IsRunningAsync followed by a request) is never dropped.
   A scripted daemon replaces the answers with fixed frames to reach error branches */
public sealed class CliDummyDaemon : IAsyncDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private readonly TempDirectory _temp = new();
    private readonly ServiceProvider _services;
    private readonly DaemonServer _server;
    private readonly SettingsManager _settings;
    private readonly Func<DaemonRequest, IReadOnlyList<(DaemonOpcode Opcode, byte[] Payload)>>? _respond;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<DaemonRequest> _requests = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private bool _stopRequested;

    public FakeTtsEngine Engine { get; } = new();
    public ModelMetadata Model { get; }

    /* Every request received, in order */
    public IReadOnlyList<DaemonRequest> Requests
    {
        get { lock (_requests) return _requests.ToList(); }
    }

    /* Completes when the accept loop exits (kill request or disposal) */
    public Task RunTask { get; private set; } = Task.CompletedTask;

    /* Wires a DaemonServer over fakes; the model has no files, so it always counts as downloaded */
    private CliDummyDaemon(bool withModel, Func<DaemonRequest, IReadOnlyList<(DaemonOpcode Opcode, byte[] Payload)>>? respond)
    {
        _respond = respond;
        var repository = new FakeConfigurationRepository();
        repository.AppConfig.CustomModelsDirectory = _temp.CreateSubdirectory("models");
        var registry = new FakeModelRegistry();
        var factory = new FakeTtsEngineFactory();
        Model = registry.AddModel("daemon-engine", "resident-model");
        factory.Engines[Model.Id] = () => Engine;
        if (withModel)
            repository.AppConfig.ActiveModel = Model.Id;

        var services = new ServiceCollection();
        services.AddSingleton<IConfigurationRepository>(repository);
        services.AddSingleton<IModelRegistry>(registry);
        services.AddSingleton<IModelDownloader>(new FakeModelDownloader());
        services.AddSingleton<ITtsEngineFactory>(factory);
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<DictionaryManager>();
        services.AddSingleton<ITtsEngineProvider>(sp => new TtsEngineProvider(
            factory, sp.GetRequiredService<SettingsManager>(), sp.GetRequiredService<DownloadManager>(), registry));
        services.AddSingleton<DaemonServer>();
        _services = services.BuildServiceProvider();
        _server = _services.GetRequiredService<DaemonServer>();
        _settings = _services.GetRequiredService<SettingsManager>();
    }

    /* Starts a daemon with (or without) a resident model and waits until it listens */
    public static async Task<CliDummyDaemon> StartAsync(bool withModel = true, Action<FakeTtsEngine>? configureEngine = null)
    {
        var daemon = new CliDummyDaemon(withModel, null);
        configureEngine?.Invoke(daemon.Engine);
        await daemon.StartListeningAsync();
        return daemon;
    }

    /* Starts a daemon answering every request with the frames `respond` returns, and waits until it listens */
    public static async Task<CliDummyDaemon> StartScriptedAsync(
        Func<DaemonRequest, IReadOnlyList<(DaemonOpcode Opcode, byte[] Payload)>> respond)
    {
        var daemon = new CliDummyDaemon(false, respond);
        await daemon.StartListeningAsync();
        return daemon;
    }

    /* Encodes a Response frame */
    public static (DaemonOpcode, byte[]) Response(DaemonResponse response) =>
        (DaemonOpcode.Response, JsonSerializer.SerializeToUtf8Bytes(response, AppJsonContext.Default.DaemonResponse));

    /* Runs the accept loop in the background and waits for its socket */
    private async Task StartListeningAsync()
    {
        var token = _cts.Token;
        RunTask = Task.Run(() => ServeAsync(token), CancellationToken.None);
        await CliTestEnvironment.WaitForDaemonSocketAsync();
    }

    /* Accept loop: the next instance is created before the accepted client is handled, so the listening socket
       (and every connection queued on it) survives between clients */
    private async Task ServeAsync(CancellationToken token)
    {
        var next = CreateInstance();
        try
        {
            while (!token.IsCancellationRequested && !_stopRequested)
            {
                var current = next;
                try
                {
                    await current.WaitForConnectionAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                next = CreateInstance();
                await using (current)
                    await HandleAsync(current, token);
                if (_stopRequested)
                    return;
            }
        }
        finally
        {
            await next.DisposeAsync();
        }
    }

    /* Reads one request and answers it; connections closed without a request (IsRunningAsync probes) are ignored */
    private async Task HandleAsync(Stream stream, CancellationToken token)
    {
        try
        {
            var (opcode, payload) = await DaemonIpcProtocol.ReadFrameAsync(stream, token);
            if (opcode != DaemonOpcode.Request)
                return;

            var request = JsonSerializer.Deserialize(payload, AppJsonContext.Default.DaemonRequest)!;
            lock (_requests) _requests.Add(request);
            await RespondAsync(stream, request, token);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or OperationCanceledException)
        {
            /* The client left (probe or cancelled playback) */
        }
    }

    /* Sends the scripted frames, or the answer DaemonServer would send */
    private async Task RespondAsync(Stream stream, DaemonRequest request, CancellationToken token)
    {
        if (_respond != null)
        {
            foreach (var (opcode, payload) in _respond(request))
                await DaemonIpcProtocol.WriteFrameAsync(stream, opcode, payload, token);
            return;
        }

        switch (request.Action)
        {
            case DaemonIpcProtocol.ActionKill:
                _stopRequested = true;
                var (killOpcode, killPayload) = Response(new DaemonResponse { Success = true });
                await DaemonIpcProtocol.WriteFrameAsync(stream, killOpcode, killPayload, token);
                break;

            case DaemonIpcProtocol.ActionStatus:
                int elapsed = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
                var (statusOpcode, statusPayload) = Response(new DaemonResponse
                {
                    Success = true,
                    ActiveModel = _settings.CurrentAppConfig.ActiveModel.ToString(),
                    IdleSecondsRemaining = Math.Max(0, (int)IdleTimeout.TotalSeconds - elapsed)
                });
                await DaemonIpcProtocol.WriteFrameAsync(stream, statusOpcode, statusPayload, token);
                break;

            default:
                await _server.HandleSynthesizeAsync(stream, request, token);
                break;
        }
    }

    /* One instance of the daemon pipe; several may exist so one keeps listening while another serves */
    private static NamedPipeServerStream CreateInstance() => new(
        DaemonIpcProtocol.PipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    /* Stops the accept loop and releases the server, its engine and the temp folder */
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await RunTask;
        _cts.Dispose();
        await _services.DisposeAsync();
        _temp.Dispose();
    }
}
