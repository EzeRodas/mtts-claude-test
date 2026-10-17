#if !WINDOWS
using System.Runtime.CompilerServices;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Infrastructure.Audio.MacOS;

namespace MoonTTS.Infrastructure.Tests.Audio.MacOS;

/* Drives MacAudioPlayer against DummyCoreAudioApi, which answers like CoreAudio / AudioToolbox / CoreFoundation */
public sealed class MacAudioPlayerTests : IDisposable
{
    private static readonly AudioFormat Mono16 = new(22050, 1, 16);
    private static readonly byte[] Pcm = [1, 2, 3, 4, 5, 6, 7, 8];

    private readonly DummyCoreAudioApi _coreAudio = new();
    private readonly MacAudioPlayer _player;

    /* Every test gets a player on a fresh dummy with two output devices and one input-only device */
    public MacAudioPlayerTests()
    {
        _coreAudio.AddDevice(40, "MacBook Pro Speakers", "BuiltInSpeakerDevice");
        _coreAudio.AddDevice(41, "MacBook Pro Microphone", "BuiltInMicrophoneDevice", outputChannels: 0);
        _coreAudio.AddDevice(55, "USB Headset", "AppleUSBAudioEngine:Headset");
        _player = new MacAudioPlayer(_coreAudio);
    }

    /* Stops any playback and frees the dummy's native buffers */
    public void Dispose()
    {
        _player.Stop();
        _coreAudio.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* The list starts with the system default device "-1" */
    [Fact]
    public void GetAvailableDevices_Always_StartsWithSystemDefault()
    {
        var result = _player.GetAvailableDevices();

        Assert.True(result.IsSuccess);
        Assert.Equal(("-1", "Default System Device"), (result.Value![0].DeviceId, result.Value[0].DeviceName));
    }

    /* Output devices follow the default, numbered from 1 in CoreAudio order and named by CoreAudio */
    [Fact]
    public void GetAvailableDevices_OutputDevices_NumberedFromOne()
    {
        var result = _player.GetAvailableDevices();

        Assert.Equal(
            [("1", "MacBook Pro Speakers"), ("2", "USB Headset")],
            result.Value!.Skip(1).Select(d => (d.DeviceId, d.DeviceName)));
    }

    /* Devices without output channels (microphones) are not listed */
    [Fact]
    public void GetAvailableDevices_InputOnlyDevice_IsSkipped()
    {
        var result = _player.GetAvailableDevices();

        Assert.DoesNotContain(result.Value!, d => d.DeviceName == "MacBook Pro Microphone");
    }

    /* A device whose output stream configuration can't be read is not listed */
    [Fact]
    public void GetAvailableDevices_StreamConfigurationUnavailable_SkipsDevice()
    {
        _coreAudio.Devices[0].StreamConfigurationUnavailable = true;

        var result = _player.GetAvailableDevices();

        Assert.Equal(["Default System Device", "USB Headset"], result.Value!.Select(d => d.DeviceName));
    }

    /* A device without a readable name gets a generic one built from its CoreAudio id */
    [Fact]
    public void GetAvailableDevices_NameUnavailable_UsesGenericName()
    {
        _coreAudio.Devices[2].NameUnavailable = true;

        var result = _player.GetAvailableDevices();

        Assert.Equal("Audio Device 55", result.Value![2].DeviceName);
    }

    /* Without any CoreAudio device only the system default is listed */
    [Fact]
    public void GetAvailableDevices_NoDevices_ReturnsOnlySystemDefault()
    {
        _coreAudio.Devices.Clear();

        var result = _player.GetAvailableDevices();

        Assert.Equal(["-1"], result.Value!.Select(d => d.DeviceId));
    }

    /* When the device list size can't be queried only the system default is listed */
    [Fact]
    public void GetAvailableDevices_DeviceListSizeFails_ReturnsOnlySystemDefault()
    {
        _coreAudio.FailDeviceListSize = true;

        var result = _player.GetAvailableDevices();

        Assert.True(result.IsSuccess);
        Assert.Equal(["-1"], result.Value!.Select(d => d.DeviceId));
    }

    /* When the device list can't be read only the system default is listed */
    [Fact]
    public void GetAvailableDevices_DeviceListReadFails_ReturnsOnlySystemDefault()
    {
        _coreAudio.FailDeviceListData = true;

        var result = _player.GetAvailableDevices();

        Assert.True(result.IsSuccess);
        Assert.Equal(["-1"], result.Value!.Select(d => d.DeviceId));
    }

    /* An exception from the native layer (e.g. the framework is missing) becomes a failed Result */
    [Fact]
    public void GetAvailableDevices_NativeLayerThrows_ReturnsFailure()
    {
        _coreAudio.ThrowOnDeviceList = new DllNotFoundException("CoreAudio");

        var result = _player.GetAvailableDevices();

        Assert.True(result.IsFailure);
        Assert.Equal("Failed to retrieve macOS CoreAudio devices: CoreAudio", result.ErrorMessage);
        Assert.IsType<DllNotFoundException>(result.Exception);
    }

    /* Every device name / UID CFString handed out by CoreAudio is released */
    [Fact]
    public void GetAvailableDevices_Always_ReleasesDeviceStrings()
    {
        _player.GetAvailableDevices();

        Assert.Equal(0, _coreAudio.LiveStringCount);
    }

    /* Selecting devices always succeeds */
    [Fact]
    public void SetOutputDevices_AnyIds_ReturnsSuccess()
    {
        var result = _player.SetOutputDevices("2", "1");

        Assert.True(result.IsSuccess);
    }

    /* A null primary disables it, so with no monitor nothing is played */
    [Fact]
    public async Task SetOutputDevices_NullPrimary_DisablesPrimary()
    {
        _player.SetOutputDevices(null, null);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Empty(_coreAudio.Queues);
    }

    /* KeepCurrent leaves an output unchanged while the other one changes */
    [Fact]
    public async Task SetOutputDevices_KeepCurrent_KeepsPreviousDevice()
    {
        _player.SetOutputDevices("2", null);

        _player.SetOutputDevices(IAudioPlayer.KeepCurrent, "1");
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(["AppleUSBAudioEngine:Headset", "BuiltInSpeakerDevice"], _coreAudio.Queues.Select(q => q.DeviceUid));
    }

    /* A blank monitor id disables the monitor output */
    [Fact]
    public async Task SetOutputDevices_BlankMonitor_DisablesMonitor()
    {
        _player.SetOutputDevices(IAudioPlayer.KeepCurrent, "1");

        _player.SetOutputDevices(IAudioPlayer.KeepCurrent, "   ");
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Single(_coreAudio.Queues);
    }

    /* A volume set before playback becomes the initial volume of the next queue (0-100 -> 0.0-1.0) */
    [Fact]
    public async Task SetVolume_BeforePlayback_AppliesToNextQueue()
    {
        _player.SetVolume(primaryVolume: 50);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(0.5f, _coreAudio.Queues[0].Volume);
    }

    /* Volumes outside 0-100 are clamped */
    [Theory]
    [InlineData(150, 1.0f)]
    [InlineData(-20, 0.0f)]
    public async Task SetVolume_OutOfRange_IsClamped(int volume, float expected)
    {
        _player.SetVolume(primaryVolume: volume);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(expected, _coreAudio.Queues[0].Volume);
    }

    /* A null volume keeps the current value */
    [Fact]
    public async Task SetVolume_NullPrimary_KeepsCurrentValue()
    {
        _player.SetVolume(primaryVolume: 30);

        _player.SetVolume(primaryVolume: null, monitorVolume: 40);
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(0.3f, _coreAudio.Queues[0].Volume);
    }

    /* Changing the primary volume during playback updates the active primary queue live */
    [Fact]
    public async Task SetVolume_DuringPlayback_UpdatesActivePrimaryQueue()
    {
        _coreAudio.AutoComplete = false;
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var queue = (await _coreAudio.WaitForStartedQueuesAsync(1, Ct))[0];

        _player.SetVolume(primaryVolume: 25);

        Assert.Equal(0.25f, queue.Volume);
        await _coreAudio.CompleteAsync(queue);
        await playback;
    }

    /* Changing the monitor volume during playback only touches the monitor queue */
    [Fact]
    public async Task SetVolume_MonitorDuringPlayback_UpdatesOnlyMonitorQueue()
    {
        _coreAudio.AutoComplete = false;
        _player.SetOutputDevices("1", "2");
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var queues = await _coreAudio.WaitForStartedQueuesAsync(2, Ct);

        _player.SetVolume(monitorVolume: 10);

        Assert.Equal(1.0f, queues[0].Volume);
        Assert.Equal(0.1f, queues[1].Volume);
        await Task.WhenAll(queues.Select(_coreAudio.CompleteAsync));
        await playback;
    }

    /* With both outputs disabled playback succeeds without creating a queue */
    [Fact]
    public async Task PlayPcmAsync_BothOutputsDisabled_SucceedsWithoutQueue()
    {
        _player.SetOutputDevices(null, null);

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(_coreAudio.Queues);
    }

    /* An empty buffer is rejected */
    [Fact]
    public async Task PlayPcmAsync_EmptyBuffer_ReturnsFailure()
    {
        var result = await _player.PlayPcmAsync(ReadOnlyMemory<byte>.Empty, Mono16, Ct);

        Assert.True(result.IsFailure);
        Assert.Equal("PCM buffer is empty.", result.ErrorMessage);
    }

    /* When CoreAudio reports the buffer as played the playback succeeds */
    [Fact]
    public async Task PlayPcmAsync_BufferPlayed_ReturnsSuccess()
    {
        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    /* The queue is described with the engine's sample rate, channels and bit depth (never a hardcoded format) */
    [Theory]
    [InlineData(22050, 1, 16)]
    [InlineData(24000, 1, 16)]
    [InlineData(48000, 2, 16)]
    [InlineData(44100, 2, 32)]
    public async Task PlayPcmAsync_AnyFormat_DescribesQueueWithEngineFormat(int sampleRate, short channels, short bitDepth)
    {
        var format = new AudioFormat(sampleRate, channels, bitDepth);

        await _player.PlayPcmAsync(new byte[format.FrameSize * 4], format, Ct);

        var asbd = _coreAudio.Queues[0].Format;
        Assert.Equal(
            ((double)sampleRate, DummyCoreAudioApi.FourCC("lpcm"), 4u | 8u, (uint)format.FrameSize, 1u, (uint)format.FrameSize, (uint)channels, (uint)bitDepth),
            (asbd.mSampleRate, asbd.mFormatID, asbd.mFormatFlags, asbd.mBytesPerPacket, asbd.mFramesPerPacket, asbd.mBytesPerFrame, asbd.mChannelsPerFrame, asbd.mBitsPerChannel));
    }

    /* Exactly the given PCM bytes are written into the AudioQueueBuffer and enqueued */
    [Fact]
    public async Task PlayPcmAsync_Buffer_EnqueuesExactPcm()
    {
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        var enqueued = Assert.Single(_coreAudio.Queues[0].EnqueuedBuffers);
        Assert.Equal(Pcm, enqueued.Pcm);
    }

    /* The queue is started after its buffer was enqueued */
    [Fact]
    public async Task PlayPcmAsync_Buffer_StartsQueue()
    {
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(1, _coreAudio.Queues[0].StartCount);
    }

    /* The system default device ("-1") plays without routing the queue to a specific device */
    [Fact]
    public async Task PlayPcmAsync_DefaultDevice_DoesNotRouteQueue()
    {
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Empty(_coreAudio.Queues[0].RequestedDeviceUids);
    }

    /* A list index ("2") routes the queue to the UID of that output device */
    [Fact]
    public async Task PlayPcmAsync_DeviceIndex_RoutesToDeviceUid()
    {
        _player.SetOutputDevices("2", null);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal("AppleUSBAudioEngine:Headset", _coreAudio.Queues[0].DeviceUid);
    }

    /* A CoreAudio UID routes the queue to that device */
    [Fact]
    public async Task PlayPcmAsync_DeviceUid_RoutesToThatDevice()
    {
        _player.SetOutputDevices("BuiltInSpeakerDevice", null);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal("BuiltInSpeakerDevice", _coreAudio.Queues[0].DeviceUid);
    }

    /* A device name (any case) routes the queue to that device's UID */
    [Fact]
    public async Task PlayPcmAsync_DeviceName_RoutesToItsUid()
    {
        _player.SetOutputDevices("usb headset", null);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal("AppleUSBAudioEngine:Headset", _coreAudio.Queues[0].DeviceUid);
    }

    /* An id that matches no device is passed to CoreAudio unchanged as a UID */
    [Fact]
    public async Task PlayPcmAsync_UnknownDevice_PassesIdThroughAsUid()
    {
        _player.SetOutputDevices("ghost-device", null);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(["ghost-device"], _coreAudio.Queues[0].RequestedDeviceUids);
    }

    /* The CFString created for routing is released */
    [Fact]
    public async Task PlayPcmAsync_RoutedDevice_ReleasesUidString()
    {
        _player.SetOutputDevices("2", null);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(0, _coreAudio.LiveStringCount);
    }

    /* With a monitor device the same PCM is played on two queues, one per device */
    [Fact]
    public async Task PlayPcmAsync_WithMonitor_PlaysOnBothDevices()
    {
        _player.SetOutputDevices("1", "2");

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(["BuiltInSpeakerDevice", "AppleUSBAudioEngine:Headset"], _coreAudio.Queues.Select(q => q.DeviceUid));
        Assert.All(_coreAudio.Queues, q => Assert.Equal(Pcm, Assert.Single(q.EnqueuedBuffers).Pcm));
    }

    /* Once played, the queue is stopped immediately and disposed */
    [Fact]
    public async Task PlayPcmAsync_Finished_StopsAndDisposesQueue()
    {
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        var queue = _coreAudio.Queues[0];
        Assert.True(queue.StoppedImmediately);
        Assert.True(queue.Disposed);
    }

    /* When CoreAudio refuses to create the queue the playback fails */
    [Fact]
    public async Task PlayPcmAsync_QueueCreationFails_ReturnsFailure()
    {
        _coreAudio.FailingNewOutputCalls.Add(1);

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create macOS AudioQueue", result.ErrorMessage);
    }

    /* When only the monitor queue can't be created the primary still plays and the failure is reported */
    [Fact]
    public async Task PlayPcmAsync_MonitorQueueCreationFails_ReturnsFailure()
    {
        _player.SetOutputDevices("1", "2");
        _coreAudio.FailingNewOutputCalls.Add(2);

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal("Failed to create macOS AudioQueue", result.ErrorMessage);
        Assert.Single(Assert.Single(_coreAudio.Queues).EnqueuedBuffers);
    }

    /* When the buffer can't be allocated the playback fails */
    [Fact]
    public async Task PlayPcmAsync_BufferAllocationFails_ReturnsFailure()
    {
        _coreAudio.FailAllocateBuffer = true;

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsFailure);
        Assert.Equal("Failed to allocate AudioQueue buffer.", result.ErrorMessage);
    }

    /* When the buffer can't be allocated the already created queue is still disposed */
    [Fact]
    public async Task PlayPcmAsync_BufferAllocationFails_DisposesQueue()
    {
        _coreAudio.FailAllocateBuffer = true;

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(_coreAudio.Queues[0].Disposed);
    }

    /* Cancelling the token while the queue plays returns the "cancelled" failure */
    [Fact]
    public async Task PlayPcmAsync_TokenCancelled_ReturnsCancelledFailure()
    {
        _coreAudio.AutoComplete = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var playback = _player.PlayPcmAsync(Pcm, Mono16, cts.Token);
        await _coreAudio.WaitForStartedQueuesAsync(1, Ct);

        cts.Cancel();
        var result = await playback;

        Assert.Equal("Playback cancelled.", result.ErrorMessage);
    }

    /* Cancelling the token stops and disposes the queue */
    [Fact]
    public async Task PlayPcmAsync_TokenCancelled_DisposesQueue()
    {
        _coreAudio.AutoComplete = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var playback = _player.PlayPcmAsync(Pcm, Mono16, cts.Token);
        var queue = (await _coreAudio.WaitForStartedQueuesAsync(1, Ct))[0];

        cts.Cancel();
        await playback;

        Assert.True(queue.Disposed);
    }

    /* Stop() during playback ends it with the "cancelled" failure */
    [Fact]
    public async Task PlayPcmAsync_StoppedDuringPlayback_ReturnsCancelledFailure()
    {
        _coreAudio.AutoComplete = false;
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        await _coreAudio.WaitForStartedQueuesAsync(1, Ct);

        _player.Stop();
        var result = await playback;

        Assert.Equal("Playback cancelled.", result.ErrorMessage);
    }

    /* Every chunk is played in order, one queue per chunk */
    [Fact]
    public async Task StreamPcmAsync_SeveralChunks_PlaysEachInOrder()
    {
        byte[][] chunks = [[1, 1], [2, 2], [3, 3]];

        var result = await _player.StreamPcmAsync(ToAsync(chunks, Ct), Mono16, Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(chunks, _coreAudio.Queues.Select(q => Assert.Single(q.EnqueuedBuffers).Pcm));
    }

    /* The first failing chunk ends the stream with its failure; later chunks are not played */
    [Fact]
    public async Task StreamPcmAsync_ChunkFails_StopsWithItsFailure()
    {
        byte[][] chunks = [[1, 1], [], [3, 3]];

        var result = await _player.StreamPcmAsync(ToAsync(chunks, Ct), Mono16, Ct);

        Assert.Equal("PCM buffer is empty.", result.ErrorMessage);
        Assert.Single(_coreAudio.Queues);
    }

    /* An empty stream succeeds without playing anything */
    [Fact]
    public async Task StreamPcmAsync_NoChunks_ReturnsSuccess()
    {
        var result = await _player.StreamPcmAsync(ToAsync([], Ct), Mono16, Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(_coreAudio.Queues);
    }

    /* Pause pauses both active queues */
    [Fact]
    public async Task Pause_DuringPlayback_PausesActiveQueues()
    {
        _coreAudio.AutoComplete = false;
        _player.SetOutputDevices("1", "2");
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var queues = await _coreAudio.WaitForStartedQueuesAsync(2, Ct);

        _player.Pause();

        Assert.All(queues, q => Assert.True(q.IsPaused));
        _player.Stop();
        await playback;
    }

    /* Pause with nothing playing makes no native call */
    [Fact]
    public void Pause_Idle_DoesNothing()
    {
        _player.Pause();

        Assert.Empty(_coreAudio.Queues);
        Assert.Equal(0, _coreAudio.InvalidQueueCalls);
    }

    /* Pause after playback finished does not touch the disposed queue */
    [Fact]
    public async Task Pause_AfterPlaybackFinished_DoesNotTouchDisposedQueue()
    {
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        _player.Pause();

        Assert.Equal(0, _coreAudio.Queues[0].PauseCount);
        Assert.Equal(0, _coreAudio.InvalidQueueCalls);
    }

    /* Resume restarts both paused queues */
    [Fact]
    public async Task Resume_AfterPause_RestartsActiveQueues()
    {
        _coreAudio.AutoComplete = false;
        _player.SetOutputDevices("1", "2");
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var queues = await _coreAudio.WaitForStartedQueuesAsync(2, Ct);
        _player.Pause();

        _player.Resume();

        Assert.All(queues, q => Assert.Equal((false, 2), (q.IsPaused, q.StartCount)));
        _player.Stop();
        await playback;
    }

    /* Resume with nothing playing makes no native call */
    [Fact]
    public void Resume_Idle_DoesNothing()
    {
        _player.Resume();

        Assert.Equal(0, _coreAudio.InvalidQueueCalls);
    }

    /* Stop during playback stops the queues immediately and disposes them */
    [Fact]
    public async Task Stop_DuringPlayback_StopsImmediatelyAndDisposesQueues()
    {
        _coreAudio.AutoComplete = false;
        _player.SetOutputDevices("1", "2");
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var queues = await _coreAudio.WaitForStartedQueuesAsync(2, Ct);

        _player.Stop();

        Assert.All(queues, q => Assert.Equal((true, true), (q.StoppedImmediately, q.Disposed)));
        await playback;
    }

    /* Stop with nothing playing makes no native call */
    [Fact]
    public void Stop_Idle_DoesNothing()
    {
        _player.Stop();

        Assert.Equal(0, _coreAudio.InvalidQueueCalls);
    }

    /* Wraps chunks into the async stream StreamPcmAsync consumes */
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ToAsync(byte[][] chunks, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }
}
#endif
