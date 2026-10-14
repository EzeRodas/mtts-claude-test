namespace MoonTTS.Domain.Entities;

public class AudioDeviceInfo
{
    /*Wrapper for audio output devices info*/
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}