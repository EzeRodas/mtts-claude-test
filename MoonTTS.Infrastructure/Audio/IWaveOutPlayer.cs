#if WINDOWS
namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - Volume : float
        - PlaybackStopped : event Action<Exception?>
        - Init(byte[] wavBytes) : void
        - Play() : void
        - Pause() : void
        - Stop() : void
        - Dispose() : void
*/

/* One waveOut device playing one WAV buffer */
public interface IWaveOutPlayer : IDisposable
{
    /* Device volume, 0.0 - 1.0 */
    float Volume { get; set; }

    /* Raised when playback ends, is stopped, or fails (with the driver error) */
    event Action<Exception?>? PlaybackStopped;

    /* Opens the device for the WAV's format and loads it */
    void Init(byte[] wavBytes);

    /* Starts or resumes playback */
    void Play();

    /* Pauses playback */
    void Pause();

    /* Stops playback (raises PlaybackStopped) */
    void Stop();
}
#endif
