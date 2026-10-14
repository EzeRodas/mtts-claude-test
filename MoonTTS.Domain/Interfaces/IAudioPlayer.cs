using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Interfaces;
public interface IAudioPlayer
{
    /* Indicator that a field should remain unchanged. Used as default in "set" methods */
    public const string KeepCurrent = "__keep_current__";
    
    /*Retrieving and setting of audio outputs*/
    Result<List<AudioDeviceInfo>> GetAvailableDevices();
    Result SetOutputDevices(string? primaryDeviceId = KeepCurrent, string? secondaryMonitorDeviceId = KeepCurrent);
    
    void SetVolume(int? primaryVolume = null, int? monitorVolume = null);
    
    /* Plays a single buffer of raw PCM audio data */
    Task<Result> PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken = default);

    /* Streams real-time raw PCM chunks directly to the configured audio output sinks */
    Task<Result> StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken = default);

    /*Pauses the currently playing audio*/
    void Pause();
    
    /*Resumes the paused audio*/
    void Resume();
    
    /*Completely stops the audio and resets the playback position*/
    void Stop();
}