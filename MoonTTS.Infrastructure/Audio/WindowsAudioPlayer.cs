/* Ohh look at me I'm Windows I need my own .net version to compile oooh */
#if WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using NAudio.Wave;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - GetAvailableDevices() : Result<List<AudioDeviceInfo>>
        - SetOutputDevices(string? primaryDeviceId, string? secondaryMonitorDeviceId) : Result
        - PlayAsync(byte[] audioBytes, CancellationToken cancellationToken) : Task<Result>
        - SetVolume(int? primaryVolume, int? monitorVolume) : void
        - Pause() : void
        - Resume() : void
        - Stop() : void
*/

public class WindowsAudioPlayer : IAudioPlayer
{
    private WaveOut? _primaryPlayer;
    private WaveOut? _monitorPlayer;

    private int _primaryDeviceNumber = -1; /* -1 is default for Windows */
    private int _monitorDeviceNumber = -2; /* -2 is disabled */
    
    private float _primaryVolume = 1.0f;
    private float _monitorVolume = 1.0f;

    private CancellationTokenSource? _playbackCts;
    private readonly Lock _processLock = new();

    /*Returns a list with all the available Windows audio output devices visible by the application*/
    public Result<List<AudioDeviceInfo>> GetAvailableDevices()
    {
        try
        {
            List<AudioDeviceInfo> devices = new List<AudioDeviceInfo>();

            /* Adds them one by one to the list */
            for (int i = -1; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                devices.Add(new AudioDeviceInfo
                {
                    DeviceId = i.ToString(),
                    DeviceName = caps.ProductName,
                });
            }

            if (!devices.Any())
            {
                return Result<List<AudioDeviceInfo>>.Failure("No available audio devices found.");
            }

            return Result<List<AudioDeviceInfo>>.Success(devices);
        }
        catch (Exception ex)
        {
            return Result<List<AudioDeviceInfo>>.Failure(
                $"Failed to retrieve Windows audio devices: {ex.Message}", ex);
        }
    }

    /*Sets the primary and monitoring (If enabled) audio device output*/
    public Result SetOutputDevices(
        string? primaryDeviceId = IAudioPlayer.KeepCurrent, 
        string? monitorDeviceId = IAudioPlayer.KeepCurrent)
    {
        lock (_processLock)
        {
            if (primaryDeviceId != IAudioPlayer.KeepCurrent)
            {
                /*Sets the main output if it is not null*/
                if (!string.IsNullOrEmpty(primaryDeviceId))
                {
                    if (!int.TryParse(primaryDeviceId, out int primaryId))
                        return Result.Failure($"Invalid primary audio device ID '{primaryDeviceId}'");
                    /*Gets the ID from the primaryDeviceId*/
                    _primaryDeviceNumber = primaryId;
                }
                else
                {
                    _primaryDeviceNumber = -2; /* Disabled */
                }
            }

            if (monitorDeviceId != IAudioPlayer.KeepCurrent)
            {
                /*Sets the monitoring output if it is not null*/
                if (monitorDeviceId != null)
                {
                    if  (!int.TryParse(monitorDeviceId, out int monitorId))
                        return Result.Failure($"Invalid monitor audio device ID '{monitorDeviceId}'");
                    _monitorDeviceNumber = monitorId;
                }
                else _monitorDeviceNumber = -2;
            }
        }

        return Result.Success();
    }

    /* Converts raw PCM to WAV and plays it via Windows audio output */
    public async Task<Result> PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken = default)
    {
        byte[] wav = WavWriter.CreateWav(pcmBytes.Span, format);
        return await PlayAsync(wav, cancellationToken);
    }

    /* Streams incoming PCM chunks sequentially on Windows */
    public async Task<Result> StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in pcmChunks.WithCancellation(cancellationToken))
        {
            var res = await PlayPcmAsync(chunk, format, cancellationToken);
            if (res.IsFailure) return res;
        }
        return Result.Success();
    }

    /* The self-explanatory ones */

    public void SetVolume(int? primaryVolume = null, int? monitorVolume = null)
    {
        lock (_processLock)
        {
            if (primaryVolume.HasValue)
            {
                _primaryVolume = Math.Clamp(primaryVolume.Value, 0, 100) / 100.0f;
                if (_primaryPlayer != null) _primaryPlayer.Volume = _primaryVolume;
            }

            if (!monitorVolume.HasValue) return;
            _monitorVolume = Math.Clamp(monitorVolume.Value, 0, 100) / 100.0f;
            if (_monitorPlayer != null) _monitorPlayer.Volume = _monitorVolume;
        }
    }
    
    public void Pause()
    {
        lock (_processLock)
        {
            _primaryPlayer?.Pause();
            _monitorPlayer?.Pause();
        }
    }

    public void Resume()
    {
        lock (_processLock)
        {
            _primaryPlayer?.Play();
            _monitorPlayer?.Play();
        }
    }
    
    public void Stop()
    {
        lock (_processLock)
        {
            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            _playbackCts = null;
            
            _primaryPlayer?.Stop();
            _monitorPlayer?.Stop();
            
            /* Process gets disposed after finished */
            _primaryPlayer?.Dispose();
            _monitorPlayer?.Dispose();

            _primaryPlayer = null;
            _monitorPlayer = null;
        }
    }
}
#endif