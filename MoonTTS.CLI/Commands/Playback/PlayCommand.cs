using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MoonTTS.CLI.Utilities;
using MoonTTS.Core.Services;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Infrastructure.Daemon;

namespace MoonTTS.CLI.commands;

/* Synthesizes text given into speech with default or custom parameters */

public static class PlayCommand
{
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("play", "Synthesizes and plays the provided text.");

        /* Options */
        var textArg = new Argument<string>("text") { Description = "Text to be synthesized." };
        var volOption = new Option<int?>("-v", "--volume") { Description = "Sets the volume level from 0 to 100." };
        var primaryVolOption = new Option<int?>("-p", "--primary-volume")
            { Description = "Sets the volume level for the main output" };
        var monitorVolOption = new Option<int?>("-m", "--monitor-volume")
            { Description = "Sets the volume level for the monitor output" };
        var speedOption = new Option<float?>("-s", "--speed")
            { Description = "Sets the audio speed from 0.0 to 2.0 if supported." };
        var outputOption = new Option<string?>("-o", "--output")
            { Description = "Optional file path to save generated .wav file." };
        
        command.Add(textArg);
        command.Add(volOption);
        command.Add(primaryVolOption);
        command.Add(monitorVolOption);
        command.Add(speedOption);
        command.Add(outputOption);
        
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string text = parseResult.GetValue(textArg) ?? string.Empty;
            int? volume = parseResult.GetValue(volOption);
            int? primaryVol = parseResult.GetValue(primaryVolOption) ?? volume;
            int? monitorVol = parseResult.GetValue(monitorVolOption) ?? volume;
            float? speed = parseResult.GetValue(speedOption);
            string? output = parseResult.GetValue(outputOption);

            var pipeline = services.GetRequiredService<ISpeechPipeline>();
            var daemonClient = services.GetRequiredService<DaemonClient>();
            var audioPlayer = services.GetRequiredService<IAudioPlayer>();
            var settingsManager = services.GetRequiredService<SettingsManager>();
            
            return await ExecuteAsync(
                text, 
                primaryVol, 
                monitorVol, 
                speed, 
                output, 
                pipeline, 
                daemonClient, 
                audioPlayer, 
                settingsManager, 
                cancellationToken);
        });

        return command;
    }

    /* Does the synthesis on the daemon if present, or directly in process */
    private static async Task<int> ExecuteAsync(
        string text,
        int? primaryVol,
        int? monitorVol,
        float? speed,
        string? output,
        ISpeechPipeline pipeline,
        DaemonClient daemonClient,
        IAudioPlayer audioPlayer,
        SettingsManager settingsManager,
        CancellationToken cancellationToken)
    {
        Terminal.Info($"Sunthesizing: {text}");
        
        /* Checks if daemon active */
        if (await daemonClient.IsRunningAsync(150))
        {
            var daemonResult = await daemonClient.StreamSpeechAsync(text, speed, cancellationToken);
            if (daemonResult.IsSuccess)
            {
                /* If active routes synthesis through it */
                var (format, chunks) = daemonResult.Value;
                var config = settingsManager.CurrentAppConfig;
                
                string? primaryId = config.EnablePrimaryPlayback ? config.PrimaryAudioDeviceId : null;
                string? monitorId = config.EnableMonitorPlayback ? config.MonitorAudioDeviceId : null;
                
                audioPlayer.SetOutputDevices(primaryId, monitorId);
                audioPlayer.SetVolume(
                    primaryVol ?? config.PrimaryVolume, 
                    monitorVol ?? config.MonitorVolume);

                using var pcmAccumulator = new MemoryStream();
                
                /* Helper method */
                async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAndStream()
                {
                    await foreach (var chunk in chunks.WithCancellation(cancellationToken))
                    {
                        pcmAccumulator.Write(chunk.Span);
                        yield return chunk;
                    }
                }
                
                var playResult = await audioPlayer.StreamPcmAsync(CaptureAndStream(), format, cancellationToken);
                if (playResult.IsFailure)
                {
                    Terminal.Error($"Error: {playResult.ErrorMessage ?? "Daemon playback failed."}");
                    return 1;
                }

                /* Writes to file if specified */
                if (!string.IsNullOrWhiteSpace(output) && pcmAccumulator.Length > 0)
                {
                    byte[] wav = WavWriter.CreateWav(pcmAccumulator.ToArray(), format);
                    await File.WriteAllBytesAsync(output, wav, cancellationToken);
                }

                var duration = format.GetDuration((int)pcmAccumulator.Length);
                Terminal.Success($"Speech synthesized via daemon successfully ({duration.TotalSeconds:F1}s)");
                return 0;
            }
        }
        
        /* Fallback for when the daemon is disabled */
        var options = new SpeechOptions
        {
            Speed = speed,
            PrimaryVolume = primaryVol,
            MonitorVolume = monitorVol,
            ExportPath = output
        };

        var result = await pipeline.SpeakAsync(text, options, cancellationToken);
        if (result.IsFailure)
        {
            Terminal.Error($"Error: {result.ErrorMessage ?? result.Exception?.Message ?? "Playback failed."}");
            return 1;
        }

        Terminal.Success($"Speech synthesized successfully ({result.Value?.AudioDuration.TotalSeconds ?? 0:F1}s)");
        return 0;
    }
}
