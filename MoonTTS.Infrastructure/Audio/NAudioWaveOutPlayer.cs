#if WINDOWS
using NAudio.Wave;

namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - NAudioWaveOutPlayer(int deviceNumber)
        - Volume : float
        - PlaybackStopped : event Action<Exception?>
        - Init(byte[] wavBytes) : void
        - Play() : void
        - Pause() : void
        - Stop() : void
        - Dispose() : void
*/

/* Real IWaveOutPlayer: an NAudio WaveOut (event callbacks, so it needs no UI message loop) reading a WAV buffer */
internal sealed class NAudioWaveOutPlayer : IWaveOutPlayer
{
    private readonly WaveOut _waveOut;
    private WaveFileReader? _reader;

    public event Action<Exception?>? PlaybackStopped;

    /* Binds a WaveOut to the device and forwards its stop notification */
    public NAudioWaveOutPlayer(int deviceNumber)
    {
        _waveOut = new WaveOut { DeviceNumber = deviceNumber };
        _waveOut.PlaybackStopped += (_, args) => PlaybackStopped?.Invoke(args.Exception);
    }

    /* Forwards to WaveOut.Volume */
    public float Volume
    {
        get => _waveOut.Volume;
        set => _waveOut.Volume = value;
    }

    /* Parses the WAV header (format comes from the buffer, never hardcoded) and opens the device for it */
    public void Init(byte[] wavBytes)
    {
        _reader = new WaveFileReader(new MemoryStream(wavBytes, writable: false));
        _waveOut.Init(_reader);
    }

    /* Forwards to WaveOut.Play (also resumes after a pause) */
    public void Play() => _waveOut.Play();

    /* Forwards to WaveOut.Pause */
    public void Pause() => _waveOut.Pause();

    /* Forwards to WaveOut.Stop */
    public void Stop() => _waveOut.Stop();

    /* Closes the device and the WAV reader */
    public void Dispose()
    {
        _waveOut.Dispose();
        _reader?.Dispose();
    }
}
#endif
