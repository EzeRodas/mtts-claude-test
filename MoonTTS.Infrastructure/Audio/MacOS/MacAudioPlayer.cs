#if !WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Infrastructure.Audio.MacOS;

/* File Structure:
        - MacDevice(string Uid, string Name)
        - MacAudioPlayer()
        - MacAudioPlayer(ICoreAudioApi coreAudio)
        - GetAvailableDevices() : Result<List<AudioDeviceInfo>>
        - SetOutputDevices(string? primaryDeviceId, string? secondaryMonitorDeviceId) : Result
        - SetVolume(int? primaryVolume, int? monitorVolume) : void
        - PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken) : Task<Result>
        - StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken) : Task<Result>
        - PlayQueueStreamAsync(byte[] pcmBytes, AudioFormat format, string? deviceUid, float volume, bool isPrimary, CancellationToken token) : Task<Result>
        - Pause() : void
        - Resume() : void
        - Stop() : void
        - EnumerateOutputDevices() : List<MacDevice>
        - ResolveDeviceUid(string? deviceId) : string? 
*/

public class MacAudioPlayer : IAudioPlayer
{
    /* Native CoreAudio / AudioToolbox / CoreFoundation surface (the real frameworks in the app, a dummy in tests) */
    private readonly ICoreAudioApi _api;

    private string? _primaryDeviceId = "-1";
    private string? _monitorDeviceId = null;
    
    
    private float _primaryVolume = 1.0f;
    private float _monitorVolume = 1.0f;

    private IntPtr _primaryQueue = IntPtr.Zero;
    private IntPtr _monitorQueue = IntPtr.Zero;

    private CancellationTokenSource? _playbackCts;
    private readonly Lock _processLock = new();

    public record MacDevice(string Uid, string Name);

    /* Plays through the real macOS frameworks */
    [SupportedOSPlatform("macos")]
    public MacAudioPlayer() : this(new MacCoreAudioApi())
    {
    }

    /* Plays through the given CoreAudio API implementation */
    public MacAudioPlayer(ICoreAudioApi coreAudio)
    {
        _api = coreAudio;
    }
    
    /* Device discovery */

    /* Lists the system default device ("-1") followed by every output device, numbered from 1 */
    public Result<List<AudioDeviceInfo>> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>()
        {
            new AudioDeviceInfo { DeviceId = "-1", DeviceName = "Default System Device" }
        };

        try
        {
            var macDevices = EnumerateOutputDevices();
            int index = 1;
            foreach (var dev in macDevices)
            {
                devices.Add(new AudioDeviceInfo
                {
                    DeviceId = (index++).ToString(),
                    DeviceName = dev.Name
                });
            }

            return Result<List<AudioDeviceInfo>>.Success(devices);
        }
        catch (Exception ex)
        {
            return Result<List<AudioDeviceInfo>>.Failure($"Failed to retrieve macOS CoreAudio devices: {ex.Message}", ex);
        }
    }
    
    /* Configuration */

    /* Selects the primary and monitor devices (KeepCurrent keeps one, null / blank disables it) */
    public Result SetOutputDevices(
        string? primaryDeviceId = IAudioPlayer.KeepCurrent, 
        string? secondaryMonitorDeviceId = IAudioPlayer.KeepCurrent)
    {
        lock (_processLock)
        {
            if (primaryDeviceId != IAudioPlayer.KeepCurrent)
            {
                _primaryDeviceId = !string.IsNullOrWhiteSpace(primaryDeviceId) ? primaryDeviceId : null;
            }

            if (secondaryMonitorDeviceId != IAudioPlayer.KeepCurrent)
            {
                _monitorDeviceId = !string.IsNullOrWhiteSpace(secondaryMonitorDeviceId)
                    ? secondaryMonitorDeviceId
                    : null;
            }
        }

        return Result.Success();
    }

    /* Sets the volumes (0-100, clamped); active queues are updated live */
    public void SetVolume(int? primaryVolume = null, int? monitorVolume = null)
    {
        lock (_processLock)
        {
            if (primaryVolume.HasValue)
            {
                _primaryVolume = Math.Clamp(primaryVolume.Value, 0, 100) / 100.0f;
                /* Live mid-playback volume update on active CoreAudio queue */
                if (_primaryQueue != IntPtr.Zero)
                    _api.AudioQueueSetParameter(_primaryQueue, ICoreAudioApi.kAudioQueueParam_Volume, _primaryVolume);
            }
            
            if (monitorVolume.HasValue)
            {
                _monitorVolume = Math.Clamp(monitorVolume.Value, 0, 100) / 100.0f;
                /* Live mid-playback volume update on active CoreAudio queue */
                if (_monitorQueue != IntPtr.Zero)
                    _api.AudioQueueSetParameter(
                        _monitorQueue, 
                        ICoreAudioApi.kAudioQueueParam_Volume,
                        _monitorVolume);
            }
            

        }
    }
    
    /* Playback */

    /* Plays one PCM buffer on the primary and monitor devices at the same time, completing when both finish */
    public async Task<Result> PlayPcmAsync(ReadOnlyMemory<byte> pcmBytes, AudioFormat format, CancellationToken cancellationToken = default)
    {
        string? primaryId;
        string? monitorId;
        float pVol, mVol;

        lock (_processLock)
        {
            primaryId = _primaryDeviceId;
            monitorId = _monitorDeviceId;
            pVol = _primaryVolume;
            mVol = _monitorVolume;
        }

        bool playPrimary = !string.IsNullOrEmpty(primaryId);
        bool playMonitor = !string.IsNullOrEmpty(monitorId);

        if (!playPrimary && !playMonitor)
            return Result.Success();

        if (pcmBytes.Length == 0)
            return Result.Failure("PCM buffer is empty.");

        Stop();

        CancellationToken linkedToken;
        lock (_processLock)
        {
            _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedToken = _playbackCts.Token;
        }

        byte[] rawArray = pcmBytes.ToArray();

        try
        {
            var tasks = new List<Task<Result>>();

            if (playPrimary)
            {
                string? targetUid = ResolveDeviceUid(primaryId);
                tasks.Add(PlayQueueStreamAsync(rawArray, format, targetUid, pVol, isPrimary: true, linkedToken));
            }

            if (playMonitor)
            {
                string? targetUid = ResolveDeviceUid(monitorId);
                tasks.Add(PlayQueueStreamAsync(rawArray, format, targetUid, mVol, isPrimary: false, linkedToken));
            }

            var results = await Task.WhenAll(tasks);

            /* Result is a struct: FirstOrDefault would return a default (failed) Result when every device succeeded */
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

    /* Sequentially plays incoming PCM chunks on macOS */
    public async Task<Result> StreamPcmAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> pcmChunks, AudioFormat format, CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in pcmChunks.WithCancellation(cancellationToken))
        {
            var result = await PlayPcmAsync(chunk, format, cancellationToken);
            if (result.IsFailure) return result;
        }
        return Result.Success();
    }


    /* Creates an AudioQueue for one device, plays the buffer on it and completes when the queue reports it played */
    private Task<Result> PlayQueueStreamAsync(
        byte[] pcmBytes,
        AudioFormat format,
        string? deviceUid,
        float volume,
        bool isPrimary,
        CancellationToken token)
    {
        var tcs = new TaskCompletionSource<Result>();

        /* Format: packed signed-integer PCM exactly as described by the engine's output format */
        var asbd = new ICoreAudioApi.AudioStreamBasicDescription
        {
            mSampleRate = format.SampleRate,
            mFormatID = ICoreAudioApi.kAudioFormatLinearPCM,
            mFormatFlags = ICoreAudioApi.kAudioFormatFlagIsSignedInteger | ICoreAudioApi.kAudioFormatFlagIsPacked,
            mBytesPerPacket = (uint)format.FrameSize,
            mFramesPerPacket = 1,
            mBytesPerFrame = (uint)format.FrameSize,
            mChannelsPerFrame = (uint)format.Channels,
            mBitsPerChannel = (uint)format.BitDepth,
            mReserved = 0
        };

        ICoreAudioApi.AudioQueueOutputCallback callback = (userData, queue, buffer) =>
        {
            /* Fired when the audio buffer completes playback */
            tcs.TrySetResult(Result.Success());
        };

        if (_api.AudioQueueNewOutput(ref asbd, callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0,
                out IntPtr queue) != 0)
        {
            return Task.FromResult(Result.Failure("Failed to create macOS AudioQueue"));
        }

        lock (_processLock)
        {
            if (isPrimary) _primaryQueue = queue;
            else _monitorQueue = queue;
        }

        /* Routes to target CoreAudio device UID */
        if (!string.IsNullOrEmpty(deviceUid))
        {
            IntPtr cfUid = _api.StringToCFString(deviceUid);
            try
            {
                _api.AudioQueueSetProperty(
                    queue,
                    ICoreAudioApi.kAudioQueueProperty_CurrentDevice,
                    ref cfUid,
                    (uint)IntPtr.Size);
            }
            finally
            {
                _api.CFRelease(cfUid);
            }
        }
        
        /* Sets initial volume */
        _api.AudioQueueSetParameter(queue, ICoreAudioApi.kAudioQueueParam_Volume, volume);
        
        /* Allocates and populates the buffer */
        if (_api.AudioQueueAllocateBuffer(queue, (uint)pcmBytes.Length, out IntPtr bufferPtr) != 0)
        {
            return Task.FromResult(Result.Failure("Failed to allocate AudioQueue buffer."));
        }
        
        /* Writes PCM data to buffer pointer (field offsets come from the AudioQueueBuffer layout, which is
           pointer-aligned, so they differ between 32 and 64-bit) */
        var queueBuffer = Marshal.PtrToStructure<ICoreAudioApi.AudioQueueBuffer>(bufferPtr);
        Marshal.Copy(pcmBytes, 0, queueBuffer.mAudioData, pcmBytes.Length);
        queueBuffer.mAudioDataByteSize = (uint)pcmBytes.Length; /* mAudioDataSize */
        Marshal.StructureToPtr(queueBuffer, bufferPtr, false);

        _api.AudioQueueEnqueueBuffer(queue, bufferPtr, 0, IntPtr.Zero);
        _api.AudioQueueStart(queue, IntPtr.Zero);
        
        /* Cancellation token registration */
        token.Register(() =>
        {
            tcs.TrySetCanceled(token);
        });

        return tcs.Task;
    }

    /* Pauses the active queues */
    public void Pause()
    {
        lock (_processLock)
        {
            if (_primaryQueue != IntPtr.Zero) _api.AudioQueuePause(_primaryQueue);
            if (_monitorQueue != IntPtr.Zero) _api.AudioQueuePause(_monitorQueue);
        }
    }

    /* Restarts the paused queues */
    public void Resume()
    {
        lock (_processLock)
        {
            if (_primaryQueue != IntPtr.Zero) _api.AudioQueueStart(_primaryQueue, IntPtr.Zero);
            if (_monitorQueue != IntPtr.Zero) _api.AudioQueueStart(_monitorQueue, IntPtr.Zero);
        }
    }
    
    /* Cancels the current playback and stops + disposes the active queues */
    public void Stop()
    {
        lock (_processLock)
        {
            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            _playbackCts = null;

            if (_primaryQueue != IntPtr.Zero)
            {
                _api.AudioQueueStop(_primaryQueue, true);
                _api.AudioQueueDispose(_primaryQueue, true);
                _primaryQueue = IntPtr.Zero;
            }

            if (_monitorQueue != IntPtr.Zero)
            {
                _api.AudioQueueStop(_monitorQueue, true);
                _api.AudioQueueDispose(_monitorQueue, true);
                _monitorQueue = IntPtr.Zero;
            }
        }
    }

    /* Returns every CoreAudio device that has at least one output channel, with its name and UID */
    private List<MacDevice> EnumerateOutputDevices()
    {
        var result = new List<MacDevice>();

        var address = new ICoreAudioApi.AudioObjectPropertyAddress
        {
            mSelector = ICoreAudioApi.kAudioHardwarePropertyDevices,
            mScope = ICoreAudioApi.kAudioObjectPropertyScopeGlobal,
            mElement = ICoreAudioApi.kAudioObjectPropertyElementMain
        };

        if (_api.AudioObjectGetPropertyDataSize(
                ICoreAudioApi.kAudioObjectSystemObject, ref address, 0, IntPtr.Zero, out uint dataSize) != 0)
        {
            return result;
        }
        
        int count = (int)(dataSize / sizeof(uint));
        uint[] deviceIds = new uint[count];
        GCHandle handle = GCHandle.Alloc(deviceIds, GCHandleType.Pinned);

        try
        {
            if (_api.AudioObjectGetPropertyData(
                    ICoreAudioApi.kAudioObjectSystemObject, 
                    ref address, 
                    0, 
                    IntPtr.Zero, 
                    ref dataSize,
                    handle.AddrOfPinnedObject()) != 0)
            {
                return result;
            }
        }
        finally
        {
            handle.Free();
        }

        foreach (uint devId in deviceIds)
        {
            var streamAddr = new ICoreAudioApi.AudioObjectPropertyAddress
            {
                mSelector = ICoreAudioApi.kAudioDevicePropertyStreamConfiguration,
                mScope = ICoreAudioApi.kAudioObjectPropertyScopeOutput,
                mElement = ICoreAudioApi.kAudioObjectPropertyElementMain
            };

            if (_api.AudioObjectGetPropertyDataSize(
                    devId, 
                    ref streamAddr, 
                    0, 
                    IntPtr.Zero, 
                    out uint streamSize) == 0 && streamSize > 0)
            {
                IntPtr bufferListPtr = Marshal.AllocHGlobal((int)streamSize);
                try
                {
                    if (_api.AudioObjectGetPropertyData(devId, ref streamAddr, 0, IntPtr.Zero,
                            ref streamSize, bufferListPtr) == 0)
                    {
                        var bufferList = Marshal.PtrToStructure<ICoreAudioApi.AudioBufferList>(bufferListPtr);
                        if (bufferList.mNumberBuffers == 0 || bufferList.mBuffers.mNumberChannels == 0)
                            continue;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(bufferListPtr);
                }
            }
            else continue;
            
            string name = $"Audio Device {devId}";
            var nameAddr = new ICoreAudioApi.AudioObjectPropertyAddress
            {
                mSelector = ICoreAudioApi.kAudioDevicePropertyDeviceNameCFString,
                mScope = ICoreAudioApi.kAudioObjectPropertyScopeGlobal,
                mElement = ICoreAudioApi.kAudioObjectPropertyElementMain
            };

            uint ptrSize = (uint)IntPtr.Size;
            if (_api.AudioObjectGetPropertyData(
                    devId, 
                    ref nameAddr, 
                    0, 
                    IntPtr.Zero, 
                    ref ptrSize,
                    out IntPtr cfName) == 0)
            {
                name = _api.CFStringToString(cfName) ?? name;
            }
            
            string uid = devId.ToString();
            var uidAddr = new ICoreAudioApi.AudioObjectPropertyAddress
            {
                mSelector = ICoreAudioApi.kAudioDevicePropertyDeviceUID,
                mScope = ICoreAudioApi.kAudioObjectPropertyScopeGlobal,
                mElement = ICoreAudioApi.kAudioObjectPropertyElementMain
            };

            ptrSize = (uint)IntPtr.Size;
            if (_api.AudioObjectGetPropertyData(
                    devId, 
                    ref uidAddr, 
                    0, 
                    IntPtr.Zero, 
                    ref ptrSize, 
                    out IntPtr cfUid) == 0)
            {
                uid = _api.CFStringToString(cfUid) ?? uid;
            }
            result.Add(new MacDevice(uid, name));
        }

        return result;
    }

    /* Maps a device id (list index, UID or name) to a CoreAudio UID; null means the system default device */
    private string? ResolveDeviceUid(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId == "-1")
            return null; /* System default device */

        var devices = EnumerateOutputDevices();

        if (int.TryParse(deviceId, out int index) && index >= 1 && index <= devices.Count)
        {
            return devices[index - 1].Uid;
        }

        var found = devices.FirstOrDefault(d =>
            string.Equals(d.Uid, deviceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Name, deviceId, StringComparison.OrdinalIgnoreCase));

        return found?.Uid ?? deviceId;
    }
}
#endif