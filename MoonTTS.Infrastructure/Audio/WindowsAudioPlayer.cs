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
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - WindowsAudioPlayer()
        - WindowsAudioPlayer(IWaveOutApi waveOutApi)
        - GetAvailableDevices() : Result<List<AudioDeviceInfo>>
        - SetOutputDevices(string? primaryDeviceId, string? secondaryMonitorDeviceId) : Result
        - PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken) : Task<Result>
        - StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken) : Task<Result>
        - PlayAsync(byte[] audioBytes, CancellationToken cancellationToken) : Task<Result>
        - PlayOnDeviceAsync(byte[] wavBytes, int deviceNumber, float volume, bool isPrimary, CancellationToken token) : Task<Result>
        - SetVolume(int? primaryVolume, int? monitorVolume) : void
        - Pause() : void
        - Resume() : void
        - Stop() : void
*/

public class WindowsAudioPlayer : IAudioPlayer
{
    /* waveOut device number meaning "this output is disabled" */
    private const int DisabledDevice = -2;

    /* Native waveOut surface (NAudio / WinMM in the app, a dummy in tests) */
    private readonly IWaveOutApi _waveOutApi;

    private IWaveOutPlayer? _primaryPlayer;
    private IWaveOutPlayer? _monitorPlayer;

    private int _primaryDeviceNumber = -1; /* -1 is default for Windows */
    private int _monitorDeviceNumber = -2; /* -2 is disabled */
    
    private float _primaryVolume = 1.0f;
    private float _monitorVolume = 1.0f;

    private CancellationTokenSource? _playbackCts;
    private readonly Lock _processLock = new();

    /* Plays through NAudio / WinMM */
    public WindowsAudioPlayer() : this(new NAudioWaveOutApi())
    {
    }

    /* Plays through the given waveOut API implementation */
    public WindowsAudioPlayer(IWaveOutApi waveOutApi)
    {
        _waveOutApi = waveOutApi;
    }

    /*Returns a list with all the available Windows audio output devices visible by the application*/
    public Result<List<AudioDeviceInfo>> GetAvailableDevices()
    {
        try
        {
            List<AudioDeviceInfo> devices = new List<AudioDeviceInfo>();

            /* Adds them one by one to the list */
            for (int i = -1; i < _waveOutApi.DeviceCount; i++)
            {
                devices.Add(new AudioDeviceInfo
                {
                    DeviceId = i.ToString(),
                    DeviceName = _waveOutApi.GetProductName(i),
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
                    _primaryDeviceNumber = DisabledDevice; /* Disabled */
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
                else _monitorDeviceNumber = DisabledDevice;
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

    /* Plays a complete WAV buffer on the primary and (if enabled) monitor devices at the same time */
    private async Task<Result> PlayAsync(byte[] audioBytes, CancellationToken cancellationToken)
    {
        int primaryDevice;
        int monitorDevice;
        float primaryVolume, monitorVolume;

        /* Snapshots the current configuration inside the lock */
        lock (_processLock)
        {
            primaryDevice = _primaryDeviceNumber;
            monitorDevice = _monitorDeviceNumber;
            primaryVolume = _primaryVolume;
            monitorVolume = _monitorVolume;
        }

        bool playPrimary = primaryDevice != DisabledDevice;
        bool playMonitor = monitorDevice != DisabledDevice;

        /* If both disabled returns */
        if (!playPrimary && !playMonitor)
            return Result.Success();

        /* Terminates any previous playback */
        Stop();

        CancellationToken linkedToken;
        lock (_processLock)
        {
            _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedToken = _playbackCts.Token;
        }

        try
        {
            var tasks = new List<Task<Result>>();
            if (playPrimary)
                tasks.Add(PlayOnDeviceAsync(audioBytes, primaryDevice, primaryVolume, isPrimary: true, linkedToken));
            if (playMonitor)
                tasks.Add(PlayOnDeviceAsync(audioBytes, monitorDevice, monitorVolume, isPrimary: false, linkedToken));

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                if (result.IsFailure) return result;
            }
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Playback cancelled.");
        }
        finally
        {
            Stop();
        }
    }

    /* Opens one device, starts the WAV on it and completes when the device reports the end of playback */
    private Task<Result> PlayOnDeviceAsync(byte[] wavBytes, int deviceNumber, float volume, bool isPrimary, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<Result>();

        try
        {
            IWaveOutPlayer player = _waveOutApi.CreatePlayer(deviceNumber);
            lock (_processLock)
            {
                if (isPrimary) _primaryPlayer = player;
                else _monitorPlayer = player;
            }

            /* Fired by the device when the buffer ends, when it is stopped, or on a driver error */
            player.PlaybackStopped += error => tcs.TrySetResult(error == null
                ? Result.Success()
                : Result.Failure($"Windows audio playback failed on device {deviceNumber}: {error.Message}", error));

            player.Init(wavBytes);
            player.Volume = volume;
            player.Play();
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure($"Failed to start Windows audio device {deviceNumber}: {ex.Message}", ex));
        }

        /* Cancellation token registration */
        token.Register(() => tcs.TrySetCanceled(token));

        return tcs.Task;
    }

    /* The self-explanatory ones */

    /* Sets the volumes (0-100, clamped); active players are updated live */
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
    
    /* Pauses the active players */
    public void Pause()
    {
        lock (_processLock)
        {
            _primaryPlayer?.Pause();
            _monitorPlayer?.Pause();
        }
    }

    /* Resumes the paused players */
    public void Resume()
    {
        lock (_processLock)
        {
            _primaryPlayer?.Play();
            _monitorPlayer?.Play();
        }
    }
    
    /* Cancels the current playback, then stops and closes the active players */
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