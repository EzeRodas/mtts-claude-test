using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.Entities;

namespace MoonTTS.CLI.Tests.Commands.Output;

public sealed class DeviceFinderTests
{
    /* Builds a device list in the order the OS would report it */
    private static List<AudioDeviceInfo> Devices(params (string Id, string Name)[] devices) =>
        devices.Select(d => new AudioDeviceInfo { DeviceId = d.Id, DeviceName = d.Name }).ToList();

    /* Runs the internal DeviceFinder.FindDevice */
    private static AudioDeviceInfo? Find(List<AudioDeviceInfo> devices, string query) =>
        CliInternals.FindDevice(null, devices, query);

    /* A query equal to a device id returns that device */
    [Fact]
    public void FindDevice_MatchingId_ReturnsDevice()
    {
        var devices = Devices(("alsa.speakers", "Speakers"), ("alsa.headset", "Headset"));

        var found = Find(devices, "alsa.headset");

        Assert.Same(devices[1], found);
    }

    /* Id matching ignores case */
    [Fact]
    public void FindDevice_IdWithDifferentCase_ReturnsDevice()
    {
        var devices = Devices(("alsa.speakers", "Speakers"));

        var found = Find(devices, "ALSA.SPEAKERS");

        Assert.Same(devices[0], found);
    }

    /* A query equal to a device name returns that device */
    [Fact]
    public void FindDevice_MatchingExactName_ReturnsDevice()
    {
        var devices = Devices(("1", "Speakers"), ("2", "Headset"));

        var found = Find(devices, "headset");

        Assert.Same(devices[1], found);
    }

    /* An id match wins over a device whose name equals the query */
    [Fact]
    public void FindDevice_QueryIsIdOfOneAndNameOfAnother_PrefersId()
    {
        var devices = Devices(("10", "headset"), ("headset", "USB Headset"));

        var found = Find(devices, "headset");

        Assert.Same(devices[1], found);
    }

    /* An exact name match wins over an earlier device whose name only contains the query */
    [Fact]
    public void FindDevice_ExactAndPartialNameMatches_PrefersExactName()
    {
        var devices = Devices(("1", "Speakers Pro"), ("2", "Speakers"));

        var found = Find(devices, "speakers");

        Assert.Same(devices[1], found);
    }

    /* A query contained in a device name returns the first such device */
    [Fact]
    public void FindDevice_PartialName_ReturnsFirstContainingDevice()
    {
        var devices = Devices(("1", "Built-in Audio"), ("2", "USB Audio Headset"), ("3", "Audio Headset 2"));

        var found = Find(devices, "headset");

        Assert.Same(devices[1], found);
    }

    /* A query matching nothing returns null */
    [Fact]
    public void FindDevice_NoMatch_ReturnsNull()
    {
        var devices = Devices(("1", "Speakers"));

        var found = Find(devices, "bluetooth");

        Assert.Null(found);
    }

    /* An empty device list returns null */
    [Fact]
    public void FindDevice_EmptyList_ReturnsNull()
    {
        var found = Find(new List<AudioDeviceInfo>(), "anything");

        Assert.Null(found);
    }
}
