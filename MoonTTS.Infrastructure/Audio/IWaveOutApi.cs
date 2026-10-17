#if WINDOWS
namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - DeviceCount : int
        - GetProductName(int deviceNumber) : string
        - CreatePlayer(int deviceNumber) : IWaveOutPlayer
*/

/* The Windows waveOut device calls WindowsAudioPlayer makes, so the player can run against NAudio / WinMM
   (NAudioWaveOutApi) or against a dummy that answers like Windows would */
public interface IWaveOutApi
{
    /* Number of waveOut devices, not counting the -1 wave mapper (system default) */
    int DeviceCount { get; }

    /* Product name of a device; -1 is the wave mapper */
    string GetProductName(int deviceNumber);

    /* Creates a player bound to a device; the device is opened when the player is initialised */
    IWaveOutPlayer CreatePlayer(int deviceNumber);
}
#endif
