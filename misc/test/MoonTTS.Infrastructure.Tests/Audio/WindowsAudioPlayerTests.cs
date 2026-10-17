#if WINDOWS
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Infrastructure.Audio;

namespace MoonTTS.Infrastructure.Tests.Audio;

/* Drives WindowsAudioPlayer against DummyWaveOutApi, which answers like WinMM waveOut through NAudio */
public sealed class WindowsAudioPlayerTests
{
    private static readonly AudioFormat Mono16 = new(22050, 1, 16);
    private static readonly byte[] Pcm = [1, 2, 3, 4, 5, 6, 7, 8];

    private readonly DummyWaveOutApi _waveOut = new("Speakers (Realtek)", "Headset (USB)");
    private readonly WindowsAudioPlayer _player;

    /* Every test gets a player on a fresh dummy with two devices */
    public WindowsAudioPlayerTests()
    {
        _player = new WindowsAudioPlayer(_waveOut);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* The wave mapper (-1, system default) comes first, followed by every device numbered from 0 */
    [Fact]
    public void GetAvailableDevices_Devices_ListsWaveMapperThenDevices()
    {
        var result = _player.GetAvailableDevices();

        Assert.Equal(
            [("-1", DummyWaveOutApi.WaveMapperName), ("0", "Speakers (Realtek)"), ("1", "Headset (USB)")],
            result.Value!.Select(d => (d.DeviceId, d.DeviceName)));
    }

    /* Without devices only the wave mapper is listed */
    [Fact]
    public void GetAvailableDevices_NoDevices_ReturnsOnlyWaveMapper()
    {
        var player = new WindowsAudioPlayer(new DummyWaveOutApi());

        var result = player.GetAvailableDevices();

        Assert.Equal(["-1"], result.Value!.Select(d => d.DeviceId));
    }

    /* A driver error while reading the capabilities becomes a failed Result */
    [Fact]
    public void GetAvailableDevices_DriverThrows_ReturnsFailure()
    {
        _waveOut.CapabilitiesError = new InvalidOperationException("NoDriver calling waveOutGetDevCaps");

        var result = _player.GetAvailableDevices();

        Assert.Equal("Failed to retrieve Windows audio devices: NoDriver calling waveOutGetDevCaps", result.ErrorMessage);
    }

    /* Numeric device ids are accepted */
    [Fact]
    public void SetOutputDevices_NumericIds_ReturnsSuccess()
    {
        var result = _player.SetOutputDevices("0", "1");

        Assert.True(result.IsSuccess);
    }

    /* A non-numeric primary id is rejected */
    [Fact]
    public void SetOutputDevices_NonNumericPrimary_ReturnsFailure()
    {
        var result = _player.SetOutputDevices("speakers", IAudioPlayer.KeepCurrent);

        Assert.Equal("Invalid primary audio device ID 'speakers'", result.ErrorMessage);
    }

    /* A non-numeric monitor id is rejected */
    [Fact]
    public void SetOutputDevices_NonNumericMonitor_ReturnsFailure()
    {
        var result = _player.SetOutputDevices(IAudioPlayer.KeepCurrent, "headset");

        Assert.Equal("Invalid monitor audio device ID 'headset'", result.ErrorMessage);
    }

    /* A null primary disables it, so with the monitor disabled too nothing is opened */
    [Fact]
    public async Task SetOutputDevices_NullPrimary_DisablesPrimary()
    {
        _player.SetOutputDevices(null, null);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Empty(_waveOut.Players);
    }

    /* KeepCurrent leaves an output unchanged while the other one changes */
    [Fact]
    public async Task SetOutputDevices_KeepCurrent_KeepsPreviousDevice()
    {
        _player.SetOutputDevices("1", null);

        _player.SetOutputDevices(IAudioPlayer.KeepCurrent, "0");
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal([1, 0], _waveOut.Players.Select(p => p.DeviceNumber));
    }

    /* A volume set before playback becomes the volume of the next player (0-100 -> 0.0-1.0) */
    [Fact]
    public async Task SetVolume_BeforePlayback_AppliesToNextPlayer()
    {
        _player.SetVolume(primaryVolume: 40);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(0.4f, _waveOut.Players[0].Volume);
    }

    /* Volumes outside 0-100 are clamped */
    [Theory]
    [InlineData(250, 1.0f)]
    [InlineData(-5, 0.0f)]
    public async Task SetVolume_OutOfRange_IsClamped(int volume, float expected)
    {
        _player.SetVolume(primaryVolume: volume);

        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(expected, _waveOut.Players[0].Volume);
    }

    /* Changing the volume during playback updates the active players live */
    [Fact]
    public async Task SetVolume_DuringPlayback_UpdatesActivePlayers()
    {
        _waveOut.AutoFinish = false;
        _player.SetOutputDevices("0", "1");
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var players = await _waveOut.WaitForPlayingAsync(2, Ct);

        _player.SetVolume(primaryVolume: 20, monitorVolume: 70);

        Assert.Equal((0.2f, 0.7f), (players[0].Volume, players[1].Volume));
        _player.Stop();
        await playback;
    }

    /* With both outputs disabled playback succeeds without opening a device */
    [Fact]
    public async Task PlayPcmAsync_BothOutputsDisabled_SucceedsWithoutPlayer()
    {
        _player.SetOutputDevices(null, null);

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsSuccess);
    }

    /* By default the wave mapper (-1, system default) plays the audio */
    [Fact]
    public async Task PlayPcmAsync_Default_PlaysOnWaveMapper()
    {
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.Equal(-1, Assert.Single(_waveOut.Players).DeviceNumber);
    }

    /* The device reporting the end of playback makes it succeed */
    [Fact]
    public async Task PlayPcmAsync_PlaybackFinished_ReturnsSuccess()
    {
        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    /* The PCM is handed to the device as a WAV whose header carries the engine's format */
    [Fact]
    public async Task PlayPcmAsync_Pcm_WrapsItInWavWithEngineFormat()
    {
        var format = new AudioFormat(48000, 2, 16);

        await _player.PlayPcmAsync(Pcm, format, Ct);

        byte[] wav = _waveOut.Players[0].WavBytes!;
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal((short)2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(48000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal((short)16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));
        Assert.Equal(Pcm, wav[44..]);
    }

    /* With a monitor device the same WAV plays on both devices */
    [Fact]
    public async Task PlayPcmAsync_WithMonitor_PlaysOnBothDevices()
    {
        _player.SetOutputDevices("0", "1");

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal([0, 1], _waveOut.Players.Select(p => p.DeviceNumber));
    }

    /* Once finished, the players are stopped and closed */
    [Fact]
    public async Task PlayPcmAsync_Finished_DisposesPlayers()
    {
        await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(_waveOut.Players[0].Disposed);
    }

    /* A device that can't be opened makes the playback fail */
    [Fact]
    public async Task PlayPcmAsync_DeviceCannotOpen_ReturnsFailure()
    {
        _player.SetOutputDevices("7", null);

        var result = await _player.PlayPcmAsync(Pcm, Mono16, Ct);

        Assert.True(result.IsFailure);
        Assert.StartsWith("Failed to start Windows audio device 7", result.ErrorMessage);
    }

    /* A driver error reported at the end of playback makes it fail */
    [Fact]
    public async Task PlayPcmAsync_DriverErrorDuringPlayback_ReturnsFailure()
    {
        _waveOut.AutoFinish = false;
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var player = (await _waveOut.WaitForPlayingAsync(1, Ct))[0];

        player.Finish(new InvalidOperationException("device removed"));
        var result = await playback;

        Assert.StartsWith("Windows audio playback failed on device -1", result.ErrorMessage);
    }

    /* Cancelling the token during playback returns the "cancelled" failure and closes the player */
    [Fact]
    public async Task PlayPcmAsync_TokenCancelled_ReturnsCancelledFailure()
    {
        _waveOut.AutoFinish = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var playback = _player.PlayPcmAsync(Pcm, Mono16, cts.Token);
        var player = (await _waveOut.WaitForPlayingAsync(1, Ct))[0];

        cts.Cancel();
        var result = await playback;

        Assert.Equal("Playback cancelled.", result.ErrorMessage);
        Assert.True(player.Disposed);
    }

    /* Every chunk is played in order */
    [Fact]
    public async Task StreamPcmAsync_SeveralChunks_PlaysEachInOrder()
    {
        byte[][] chunks = [[1, 1], [2, 2], [3, 3]];

        var result = await _player.StreamPcmAsync(ToAsync(chunks, Ct), Mono16, Ct);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(chunks, _waveOut.Players.Select(p => p.WavBytes![44..]));
    }

    /* The first failing chunk ends the stream with its failure */
    [Fact]
    public async Task StreamPcmAsync_ChunkFails_StopsWithItsFailure()
    {
        _player.SetOutputDevices("9", null);

        var result = await _player.StreamPcmAsync(ToAsync([[1, 1], [2, 2]], Ct), Mono16, Ct);

        Assert.True(result.IsFailure);
        Assert.Single(_waveOut.Players);
    }

    /* Pause pauses the active players and Resume plays them again */
    [Fact]
    public async Task PauseResume_DuringPlayback_PauseThenPlayActivePlayers()
    {
        _waveOut.AutoFinish = false;
        _player.SetOutputDevices("0", "1");
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var players = await _waveOut.WaitForPlayingAsync(2, Ct);

        _player.Pause();
        var paused = players.Select(p => p.PauseCount).ToList();
        _player.Resume();

        Assert.Equal([1, 1], paused);
        Assert.All(players, p => Assert.Equal(2, p.PlayCount));
        _player.Stop();
        await playback;
    }

    /* Stop during playback stops and closes every player */
    [Fact]
    public async Task Stop_DuringPlayback_StopsAndDisposesPlayers()
    {
        _waveOut.AutoFinish = false;
        _player.SetOutputDevices("0", "1");
        var playback = _player.PlayPcmAsync(Pcm, Mono16, Ct);
        var players = await _waveOut.WaitForPlayingAsync(2, Ct);

        _player.Stop();

        Assert.All(players, p => Assert.Equal((1, true), (p.StopCount, p.Disposed)));
        await playback;
    }

    /* Pause, Resume and Stop with nothing playing do nothing */
    [Fact]
    public void PauseResumeStop_Idle_DoNothing()
    {
        var error = Record.Exception(() =>
        {
            _player.Pause();
            _player.Resume();
            _player.Stop();
        });

        Assert.Null(error);
        Assert.Empty(_waveOut.Players);
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

/* File Structure:
        - WaveMapperName : string
        - DummyWaveOutApi(params string[] deviceNames)
        - DeviceCount : int
        - GetProductName(int deviceNumber) : string
        - CreatePlayer(int deviceNumber) : IWaveOutPlayer
        - WaitForPlayingAsync(int count, CancellationToken cancellationToken) : Task<IReadOnlyList<DummyWaveOutPlayer>>
*/

/* IWaveOutApi that behaves like WinMM waveOut: devices 0..n-1 plus the -1 wave mapper; opening any other number
   fails with BadDeviceId; with AutoFinish a player reports the end of playback right after Play */
public sealed class DummyWaveOutApi : IWaveOutApi
{
    public const string WaveMapperName = "Microsoft Sound Mapper";

    private readonly string[] _deviceNames;
    private readonly List<DummyWaveOutPlayer> _players = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /* Creates the dummy with the given device names (device 0, 1, ...) */
    public DummyWaveOutApi(params string[] deviceNames)
    {
        _deviceNames = deviceNames;
    }

    public bool AutoFinish { get; set; } = true;
    public Exception? CapabilitiesError { get; set; }

    /* Players created so far, oldest first */
    public IReadOnlyList<DummyWaveOutPlayer> Players
    {
        get { lock (_players) return _players.ToList(); }
    }

    /* Number of devices (without the wave mapper) */
    public int DeviceCount => _deviceNames.Length;

    /* waveOutGetDevCaps product name */
    public string GetProductName(int deviceNumber)
    {
        if (CapabilitiesError != null) throw CapabilitiesError;
        return deviceNumber == -1 ? WaveMapperName : _deviceNames[deviceNumber];
    }

    /* Creates a player bound to a device number */
    public IWaveOutPlayer CreatePlayer(int deviceNumber)
    {
        var player = new DummyWaveOutPlayer(this, deviceNumber, deviceNumber >= -1 && deviceNumber < _deviceNames.Length);
        lock (_players) _players.Add(player);
        return player;
    }

    /* Completes once at least `count` players are playing */
    public async Task<IReadOnlyList<DummyWaveOutPlayer>> WaitForPlayingAsync(int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_players)
            {
                var playing = _players.Where(p => p.PlayCount > 0).ToList();
                if (playing.Count >= count)
                    return playing;
                changed = _changed.Task;
            }
            await changed.WaitAsync(cancellationToken);
        }
    }

    /* Wakes WaitForPlayingAsync */
    internal void NotifyChanged()
    {
        lock (_players)
        {
            _changed.TrySetResult();
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

/* File Structure:
        - Volume : float
        - PlaybackStopped : event Action<Exception?>
        - Init(byte[] wavBytes) : void
        - Play() : void
        - Pause() : void
        - Stop() : void
        - Finish(Exception? error) : void
        - Dispose() : void
*/

/* One waveOut handle: Init opens the device (BadDeviceId for an unknown number), Stop and the end of the buffer
   raise PlaybackStopped from a driver thread */
public sealed class DummyWaveOutPlayer : IWaveOutPlayer
{
    private readonly DummyWaveOutApi _api;
    private readonly bool _deviceExists;

    /* Created through DummyWaveOutApi.CreatePlayer */
    internal DummyWaveOutPlayer(DummyWaveOutApi api, int deviceNumber, bool deviceExists)
    {
        _api = api;
        _deviceExists = deviceExists;
        DeviceNumber = deviceNumber;
    }

    public int DeviceNumber { get; }
    public float Volume { get; set; } = 1.0f;
    public byte[]? WavBytes { get; private set; }
    public int PlayCount { get; private set; }
    public int PauseCount { get; private set; }
    public int StopCount { get; private set; }
    public bool Disposed { get; private set; }

    public event Action<Exception?>? PlaybackStopped;

    /* Opens the device for the WAV */
    public void Init(byte[] wavBytes)
    {
        if (!_deviceExists)
            throw new InvalidOperationException("BadDeviceId calling waveOutOpen");
        WavBytes = wavBytes;
    }

    /* Starts (or resumes) playback; with AutoFinish the end of the buffer is reported right away */
    public void Play()
    {
        PlayCount++;
        _api.NotifyChanged();
        if (_api.AutoFinish && PlayCount == 1)
            Finish();
    }

    /* Pauses playback */
    public void Pause() => PauseCount++;

    /* Stops playback and reports it */
    public void Stop()
    {
        StopCount++;
        Finish();
    }

    /* Reports the end of playback (or a driver error) from a driver thread */
    public void Finish(Exception? error = null)
    {
        var handler = PlaybackStopped;
        Task.Run(() => handler?.Invoke(error));
    }

    /* Closes the handle */
    public void Dispose() => Disposed = true;
}
#endif
