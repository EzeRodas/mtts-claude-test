using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Tests.Common;

/* File Structure:
        - GetAvailableDevices() : Result<List<AudioDeviceInfo>>
        - SetOutputDevices(string? primaryDeviceId, string? secondaryMonitorDeviceId) : Result
        - SetVolume(int? primaryVolume, int? monitorVolume) : void
        - PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken) : Task<Result>
        - StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken) : Task<Result>
        - Pause() : void
        - Resume() : void
        - Stop() : void
*/

/* IAudioPlayer that records every call and the PCM it was asked to play */
public sealed class FakeAudioPlayer : IAudioPlayer
{
    public List<AudioDeviceInfo> Devices { get; } = new();
    public Result<List<AudioDeviceInfo>>? DevicesResultOverride { get; set; }

    public string? PrimaryDeviceId { get; private set; }
    public string? MonitorDeviceId { get; private set; }
    public int? PrimaryVolume { get; private set; }
    public int? MonitorVolume { get; private set; }

    public List<(byte[] Pcm, AudioFormat Format)> PlayedBuffers { get; } = new();
    public List<byte[]> StreamedChunks { get; } = new();
    public AudioFormat? LastStreamFormat { get; private set; }

    /* Result returned by PlayPcmAsync / StreamPcmAsync */
    public Result PlaybackResult { get; set; } = Result.Success();

    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int StopCount { get; private set; }

    /* Returns the configured device list */
    public Result<List<AudioDeviceInfo>> GetAvailableDevices() =>
        DevicesResultOverride ?? Result<List<AudioDeviceInfo>>.Success(Devices.ToList());

    /* Records the selected devices, keeping the current one for IAudioPlayer.KeepCurrent */
    public Result SetOutputDevices(string? primaryDeviceId = IAudioPlayer.KeepCurrent, string? secondaryMonitorDeviceId = IAudioPlayer.KeepCurrent)
    {
        if (primaryDeviceId != IAudioPlayer.KeepCurrent) PrimaryDeviceId = primaryDeviceId;
        if (secondaryMonitorDeviceId != IAudioPlayer.KeepCurrent) MonitorDeviceId = secondaryMonitorDeviceId;
        return Result.Success();
    }

    /* Records the volumes (null keeps the current value) */
    public void SetVolume(int? primaryVolume = null, int? monitorVolume = null)
    {
        if (primaryVolume.HasValue) PrimaryVolume = primaryVolume;
        if (monitorVolume.HasValue) MonitorVolume = monitorVolume;
    }

    /* Records a single buffer */
    public Task<Result> PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken = default)
    {
        PlayedBuffers.Add((pcmBytes.ToArray(), format));
        return Task.FromResult(PlaybackResult);
    }

    /* Consumes and records every streamed chunk */
    public async Task<Result> StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken = default)
    {
        LastStreamFormat = format;
        await foreach (var chunk in pcmChunks.WithCancellation(cancellationToken))
            StreamedChunks.Add(chunk.ToArray());
        return PlaybackResult;
    }

    /* Counts pauses */
    public void Pause() => PauseCount++;

    /* Counts resumes */
    public void Resume() => ResumeCount++;

    /* Counts stops */
    public void Stop() => StopCount++;
}
