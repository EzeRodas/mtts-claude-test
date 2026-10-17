using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MoonTTS.Core.Services;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Constants;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Infrastructure.Daemon.DTOs;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.Infrastructure.Daemon;

/* File Structure:
        - DaemonServer(
            ITtsEngineProvider engineProvider, 
            DictionaryManager dictionaryManager, 
            SettingsManager settingsManager)
        - RunAsync(CancellationToken cancellationToken) : Task
        - CreatePipe(bool firstInstance) : NamedPipeServerStream
        - HandleClientAsync(Stream stream, CancellationToken cancellationToken) : Task
        - HandleSynthesizeAsync(Stream stream, DaemonRequest request, CancellationToken cancellationToken) : Task 
*/

public class DaemonServer
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    /* How often a wait for clients is interrupted to check whether the daemon must stop */
    private static readonly TimeSpan ConnectionPollInterval = TimeSpan.FromSeconds(2);

    /* Pause before retrying when the pipe cannot be created */
    private static readonly TimeSpan PipeRetryDelay = TimeSpan.FromSeconds(1);

    /* One instance serving a client plus one listening for the next */
    private const int MaxPipeInstances = 2;

    private readonly ITtsEngineProvider _engineProvider;
    private readonly DictionaryManager _dictionaryManager;
    private readonly SettingsManager _settingsManager;

    private DateTime _lastActivityTime = DateTime.UtcNow;
    private bool _stopRequested = false;

    public DaemonServer(
        ITtsEngineProvider engineProvider,
        DictionaryManager dictionaryManager,
        SettingsManager settingsManager)
    {
        _engineProvider = engineProvider;
        _dictionaryManager = dictionaryManager;
        _settingsManager = settingsManager;
    }

    /* Keeps the model loaded and serves clients until killed, idle for IdleTimeout or cancelled */
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _lastActivityTime = DateTime.UtcNow;

        /* Prewarms the model when the daemon spawns */
        await _engineProvider.GetEngineAsync(cancellationToken).ConfigureAwait(false);
        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(async () =>
        {
            while (!timerCts.Token.IsCancellationRequested && !_stopRequested)
            {
                await Task.Delay(5000, timerCts.Token);
                if (DateTime.UtcNow - _lastActivityTime >= IdleTimeout)
                {
                    _stopRequested = true;
                    break;
                }
            }
        }, timerCts.Token);

        /* An instance is always listening, also while a client is served, so a client connecting right after
           another one (the CLI probes the pipe, then sends its request) is queued instead of reset */
        NamedPipeServerStream? listening = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_stopRequested)
            {
                try
                {
                    listening ??= CreatePipe(firstInstance: true);

                    /* Waits for client connection with short timeout to periodically check _stopRequested */
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    waitCts.CancelAfter(ConnectionPollInterval);

                    try
                    {
                        await listening.WaitForConnectionAsync(waitCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        /* The expired instance is replaced before it is released, so the pipe never stops listening */
                        var expired = listening;
                        listening = null;
                        await using (expired.ConfigureAwait(false))
                        {
                            listening = CreatePipe(firstInstance: false);
                        }
                        continue;
                    }

                    var connected = listening;
                    listening = null;
                    await using (connected.ConfigureAwait(false))
                    {
                        listening = CreatePipe(firstInstance: false);
                        await HandleClientAsync(connected, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception) when (_stopRequested || cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (listening == null)
                {
                    /* The pipe could not be created (e.g. another daemon owns it): retries after a pause instead of
                       spinning, until the idle timeout, a cancellation or the pipe becoming available */
                    Console.Error.WriteLine($"[DaemonServer] Cannot listen on '{DaemonIpcProtocol.PipeName}': {ex.Message}");
                    try
                    {
                        await Task.Delay(PipeRetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                catch { /* Transient Pipe error */ }
            }
        }
        finally
        {
            if (listening != null)
                await listening.DisposeAsync().ConfigureAwait(false);
            timerCts.Cancel();
        }
    }

    /* Creates one instance of the daemon pipe. The first instance fails while another daemon owns the name */
    private static NamedPipeServerStream CreatePipe(bool firstInstance) =>
        new(
            DaemonIpcProtocol.PipeName,
            PipeDirection.InOut,
            MaxPipeInstances,
            PipeTransmissionMode.Byte,
            firstInstance ? PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance : PipeOptions.Asynchronous);

    /* Handle an individual client connection using binary protocol framing */
    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        (DaemonOpcode opcode, byte[] payload) frame;
        try
        {
            frame = await DaemonIpcProtocol.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (frame.opcode != DaemonOpcode.Request) return;

        var request = JsonSerializer.Deserialize(frame.payload, AppJsonContext.Default.DaemonRequest);
        if (request == null) return;

        switch (request.Action)
        {
            case DaemonIpcProtocol.ActionKill:
            {
                _stopRequested = true;
                var response = new DaemonResponse { Success = true };
                byte[] responseJson =
                    JsonSerializer.SerializeToUtf8Bytes(response, AppJsonContext.Default.DaemonResponse);
                await DaemonIpcProtocol.WriteFrameAsync(
                    stream, 
                    DaemonOpcode.Response, 
                    responseJson, 
                    cancellationToken).ConfigureAwait(false);
                break;
            }
            
            case DaemonIpcProtocol.ActionStatus:
            {
                int elapsed = (int)(DateTime.UtcNow - _lastActivityTime).TotalSeconds;
                var response = new DaemonResponse
                {
                    Success = true,
                    ActiveModel = _settingsManager.CurrentAppConfig.ActiveModel.ToString(),
                    IdleSecondsRemaining = Math.Max(0, (int)IdleTimeout.TotalSeconds - elapsed)
                };
                byte[] responseJson =
                    JsonSerializer.SerializeToUtf8Bytes(response, AppJsonContext.Default.DaemonResponse);
                await DaemonIpcProtocol.WriteFrameAsync(
                        stream, 
                        DaemonOpcode.Response, 
                        responseJson, cancellationToken)
                    .ConfigureAwait(false);
                break;
            }
            
            case DaemonIpcProtocol.ActionSynthesize:
                default:
                    await HandleSynthesizeAsync(stream, request, cancellationToken).ConfigureAwait(false);
                    break;
        }
    }

    public async Task HandleSynthesizeAsync(Stream stream, DaemonRequest request, CancellationToken cancellationToken)
    {
        _lastActivityTime = DateTime.UtcNow;
        
        _settingsManager.ReloadAppConfig();
        var targetModel = _settingsManager.CurrentAppConfig.ActiveModel;
        if (_engineProvider.CurrentModel != targetModel)
        {
            await _engineProvider.SetEngineAsync(targetModel, cancellationToken).ConfigureAwait(false);
        }

        var engine = await _engineProvider.GetEngineAsync(cancellationToken).ConfigureAwait(false);
        if (!engine.IsAvailable)
        {
            var failResponse = new DaemonResponse
            {
                Success = false,
                ErrorMessage = "TTS Model not loaded or downloaded."
            };
            byte[] failJson = JsonSerializer.SerializeToUtf8Bytes(failResponse, AppJsonContext.Default.DaemonResponse);
            await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.Response, failJson, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var format = engine.OutputFormat;
        var initialResponse = new DaemonResponse()
        {
            Success = true,
            Format = format,
            ActiveModel = targetModel.ToString()
        };
        byte[] initJson = JsonSerializer.SerializeToUtf8Bytes(initialResponse, AppJsonContext.Default.DaemonResponse);
        await DaemonIpcProtocol.WriteFrameAsync(stream, DaemonOpcode.Response, initJson, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            string processedText = _dictionaryManager.ReplaceText(request.Text);
            var sentences = processedText.Length > AppConstants.MinSplittingLenght
                ? TextSplitter.SplitIntoSentences(processedText)
                : new List<string>() { processedText };

            foreach (var sentence in sentences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await foreach (var chunk in
                               engine.SynthesizeStreamAsync(sentence, request.Speed, cancellationToken)
                                   .ConfigureAwait(false))
                {
                    await DaemonIpcProtocol
                        .WriteFrameAsync(stream, DaemonOpcode.AudioChunk, chunk, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await DaemonIpcProtocol
                .WriteFrameAsync(stream, DaemonOpcode.AudioEnd, ReadOnlyMemory<byte>.Empty, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            /* Client disconnected or cancelled */
        }
        catch (Exception ex)
        {
            byte[] errorBytes = Encoding.UTF8.GetBytes(ex.Message);
            await DaemonIpcProtocol
                .WriteFrameAsync(stream, DaemonOpcode.Error, errorBytes, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}