using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Core.Tests.Doubles;

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

/* IAudioPlayer that fails immediately without reading the stream, like a player whose device cannot be opened,
   or that throws from PlayPcmAsync, like a crashing native player */
public sealed class RejectingAudioPlayer : IAudioPlayer
{
    public const string Error = "Output device could not be opened.";

    public int StopCount { get; private set; }

    /* Awaited by StreamPcmAsync before it fails, e.g. to fail only once synthesis is under way */
    public Func<CancellationToken, Task>? BeforeFailing { get; init; }

    /* No devices */
    public Result<List<AudioDeviceInfo>> GetAvailableDevices() => Result<List<AudioDeviceInfo>>.Success(new List<AudioDeviceInfo>());

    /* Accepts any device */
    public Result SetOutputDevices(string? primaryDeviceId = IAudioPlayer.KeepCurrent, string? secondaryMonitorDeviceId = IAudioPlayer.KeepCurrent) =>
        Result.Success();

    /* Ignores volumes */
    public void SetVolume(int? primaryVolume = null, int? monitorVolume = null) { }

    /* When set, PlayPcmAsync throws it instead of returning a failure, like a crashing native player */
    public Exception? ThrowOnPlay { get; init; }

    /* Fails without playing (or throws ThrowOnPlay, when set) */
    public Task<Result> PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken = default) =>
        ThrowOnPlay != null ? throw ThrowOnPlay : Task.FromResult(Result.Failure(Error));

    /* Fails without enumerating a single chunk (after awaiting BeforeFailing, when set) */
    public async Task<Result> StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken = default)
    {
        if (BeforeFailing != null)
            await BeforeFailing(cancellationToken);
        return Result.Failure(Error);
    }

    /* Nothing to pause */
    public void Pause() { }

    /* Nothing to resume */
    public void Resume() { }

    /* Counts stops */
    public void Stop() => StopCount++;
}
