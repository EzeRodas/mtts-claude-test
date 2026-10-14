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
        - HandleClientAsync(Stream stream, CancellationToken cancellationToken) : Task
        - HandleSynthesizeAsync(Stream stream, DaemonRequest request, CancellationToken cancellationToken) : Task 
*/

public class DaemonServer
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    
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

        while (!cancellationToken.IsCancellationRequested && !_stopRequested)
        {
            try
            {
                await using var serverPipe = new NamedPipeServerStream(
                    DaemonIpcProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                /* Waits for client connection with short timeout to periodically check _stopRequested */
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                waitCts.CancelAfter(TimeSpan.FromSeconds(2));

                try
                {
                    await serverPipe.WaitForConnectionAsync(waitCts.Token);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }

                await HandleClientAsync(serverPipe, cancellationToken);
            }
            catch (Exception) when (_stopRequested || cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch { /* Transient Pipe error */ }
        }
        timerCts.Cancel();
    }

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