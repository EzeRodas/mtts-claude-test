#if WINDOWS
using NAudio.Wave;

namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - DeviceCount : int
        - GetProductName(int deviceNumber) : string
        - CreatePlayer(int deviceNumber) : IWaveOutPlayer
*/

/* Real IWaveOutApi backed by NAudio's WinMM WaveOut */
internal sealed class NAudioWaveOutApi : IWaveOutApi
{
    /* Forwards to WaveOut.DeviceCount */
    public int DeviceCount => WaveOut.DeviceCount;

    /* Forwards to WaveOut.GetCapabilities */
    public string GetProductName(int deviceNumber) => WaveOut.GetCapabilities(deviceNumber).ProductName;

    /* Creates an NAudio WaveOut bound to the device */
    public IWaveOutPlayer CreatePlayer(int deviceNumber) => new NAudioWaveOutPlayer(deviceNumber);
}
#endif
