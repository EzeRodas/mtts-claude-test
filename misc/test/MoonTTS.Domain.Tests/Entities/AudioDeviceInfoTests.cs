using MoonTTS.Domain.Entities;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies the defaults of AudioDeviceInfo */
public sealed class AudioDeviceInfoTests
{
    /* Id and name start empty instead of null */
    [Fact]
    public void Properties_Default_AreEmpty()
    {
        var device = new AudioDeviceInfo();

        Assert.Equal(string.Empty, device.DeviceId);
        Assert.Equal(string.Empty, device.DeviceName);
    }

    /* Both properties keep the values they are set to */
    [Fact]
    public void Properties_WhenSet_KeepValues()
    {
        var device = new AudioDeviceInfo { DeviceId = "alsa_output.speakers", DeviceName = "Speakers" };

        Assert.Equal("alsa_output.speakers", device.DeviceId);
        Assert.Equal("Speakers", device.DeviceName);
    }
}
